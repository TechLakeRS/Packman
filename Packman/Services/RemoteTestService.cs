using Packman.Helpers;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace Packman.Services;

/// <summary>
/// Deploys a PSADT v4 package to a test machine over WinRM.
///
/// WinRM is the transport only; the install runs from a one-shot scheduled task. A
/// remote session runs as the connecting admin, not as SYSTEM, and the two differ in
/// %TEMP%, HKCU and network identity, so it would not match what Intune does.
/// </summary>
public class RemoteTestService
{
    private const string RemoteBasePath = @"C:\Temp\Packman";
    private const string TaskName = "Packman_RemoteTest";
    private const string ExitCodeSentinel = "PACKMAN_EXIT_CODE:";

    // SCHED_S_TASK_HAS_NOT_RUN: registered but never produced a result.
    private const int NeverRan = 267011;

    /// <summary>The deployment verbs PSADT accepts.</summary>
    public static readonly IReadOnlySet<string> DeploymentTypes =
        new HashSet<string>(StringComparer.Ordinal) { "Install", "Uninstall", "Repair" };

    /// <summary>PSADT/MSI success codes; 3010 and 1641 mean reboot required.</summary>
    public static bool IsSuccess(int exitCode) => exitCode is 0 or 3010 or 1641;

    /// <summary>Computer names only: the value reaches a UNC path and a remote script.</summary>
    public static bool IsValidComputerName(string computerName) =>
        !string.IsNullOrWhiteSpace(computerName) &&
        Regex.IsMatch(computerName, @"^[A-Za-z0-9][A-Za-z0-9\.\-]{0,62}$");

    /// <summary>
    /// Runs the deployment and returns the PSADT exit code.
    /// <paramref name="runAsUser"/>: false runs as SYSTEM (what Intune does), true runs in
    /// the logged-on user's session. <paramref name="copyProgress"/> reports 0-100, then null.
    /// Throws PSRemotingTransportException when WinRM is unreachable.
    /// </summary>
    public int Deploy(string computerName, string sourcePath, string deploymentType,
        bool cleanupAfterDeploy, bool runAsUser, Action<string> output,
        Action<int?>? copyProgress = null)
    {
        if (!IsValidComputerName(computerName))
            throw new ArgumentException($"'{computerName}' is not a valid computer name", nameof(computerName));

        // Reaches the remote command line, so only the three PSADT verbs pass.
        if (!DeploymentTypes.Contains(deploymentType))
            throw new ArgumentException($"'{deploymentType}' is not a valid deployment type", nameof(deploymentType));

        output("========================================");
        output("Packman Remote Test (WinRM)");
        output("========================================");
        output($"Target: {computerName}");
        output($"Source: {sourcePath}");
        output($"Type: {deploymentType}");
        output($"Run as: {(runAsUser ? "Logged-on user" : "NT AUTHORITY\\SYSTEM")}");
        output("");

        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"Source path not found: {sourcePath}");

        string relativeScriptPath;
        if (File.Exists(Path.Combine(sourcePath, "Application", PsadtLayout.ScriptName)))
        {
            relativeScriptPath = $@"Application\{PsadtLayout.ScriptName}";
            output($"[OK] Found {PsadtLayout.ScriptName} in Application subfolder");
        }
        else if (File.Exists(Path.Combine(sourcePath, PsadtLayout.ScriptName)))
        {
            relativeScriptPath = PsadtLayout.ScriptName;
            output($"[OK] Found {PsadtLayout.ScriptName} in root folder");
        }
        else
        {
            throw new FileNotFoundException($"{PsadtLayout.ScriptName} not found in package");
        }

        // ICMP is a hint only: plenty of fleets block it while WinRM is open.
        output($"Checking connectivity to {computerName}...");
        using (var ping = new Ping())
        {
            try
            {
                var reply = ping.Send(computerName, 2000);
                output(reply.Status == IPStatus.Success
                    ? $"[OK] {computerName} responds to ping"
                    : $"[--] {computerName} did not respond to ping ({reply.Status}) - trying WinRM anyway");
            }
            catch (PingException ex)
            {
                output($"[--] Ping failed ({ex.InnerException?.Message ?? ex.Message}) - trying WinRM anyway");
            }
        }

        // Connect before the copy so a target without WinRM fails fast.
        output($"Connecting to {computerName} via WinRM...");
        var connectionInfo = new WSManConnectionInfo { ComputerName = computerName };
        using var runspace = RunspaceFactory.CreateRunspace(connectionInfo);
        runspace.Open();
        output("[OK] WinRM session established");

        // For user-context installs, fail before the copy if nobody is logged on.
        if (runAsUser)
        {
            using var check = PowerShell.Create();
            check.Runspace = runspace;
            check.AddScript("(Get-CimInstance Win32_ComputerSystem).UserName");
            string? loggedOnUser = check.Invoke().FirstOrDefault()?.ToString();
            if (string.IsNullOrEmpty(loggedOnUser))
                throw new InvalidOperationException($"No user is logged on to {computerName} — a user-context install requires a logged-on user");
            output($"[OK] Logged-on user: {loggedOnUser}");
        }

        // Layout is ...\Manufacturer_AppName\Version; combine both so packages don't mix
        // and a re-run only copies what differs.
        var sourceDir = new DirectoryInfo(sourcePath);
        string packageName = SanitiseFolderName(sourceDir.Parent?.Parent != null
            ? $"{sourceDir.Parent.Name}_{sourceDir.Name}"
            : sourceDir.Name);
        string remotePackagePath = $@"{RemoteBasePath}\{packageName}";
        string targetUnc = $@"\\{computerName}\C$\Temp\Packman\{packageName}";

        output($"Copying files to {targetUnc}...");
        var copyTimer = Stopwatch.StartNew();
        CopyWithRobocopy(sourcePath, targetUnc, copyProgress);
        copyTimer.Stop();
        copyProgress?.Invoke(null);

        var copiedFiles = Directory.GetFiles(targetUnc, "*", SearchOption.AllDirectories);
        double sizeMb = copiedFiles.Sum(f => new FileInfo(f).Length) / 1048576.0;
        output($"Package size: {Math.Round(sizeMb, 2)} MB");
        output($"[OK] {copiedFiles.Length} files in sync after {Math.Round(copyTimer.Elapsed.TotalSeconds, 1)} seconds");

        string remoteScriptPath = $@"{remotePackagePath}\{relativeScriptPath}";
        string logPath = $@"{remotePackagePath}\Packman_Deploy.log";
        string deployArgs = $"-ExecutionPolicy Bypass -NoProfile -File \"{remoteScriptPath}\" -DeploymentType {deploymentType}";

        output("");
        output($"Registering scheduled task '{TaskName}' on target...");
        output($"Command: powershell.exe {deployArgs}");
        output("");

        int exitCode = -1;
        using (var ps = PowerShell.Create())
        {
            ps.Runspace = runspace;
            ps.AddScript(BuildTaskScript(runAsUser, deployArgs, logPath));

            var stdout = new PSDataCollection<PSObject>();
            stdout.DataAdded += (s, e) =>
            {
                string? chunk = stdout[e.Index]?.ToString();
                if (string.IsNullOrEmpty(chunk)) return;
                foreach (string rawLine in chunk.Split('\n'))
                {
                    string line = rawLine.TrimEnd('\r');
                    if (line.Length == 0) continue;
                    if (line.StartsWith(ExitCodeSentinel))
                        int.TryParse(line.AsSpan(ExitCodeSentinel.Length), out exitCode);
                    else
                        output(line);
                }
            };
            ps.Streams.Error.DataAdded += (s, e) => output($"ERROR: {ps.Streams.Error[e.Index]}");

            ps.Invoke<object, PSObject>(null, stdout, null);
        }

        output("");
        output($"Exit code: {exitCode}");

        if (cleanupAfterDeploy)
        {
            output("");
            output("Cleaning up...");
            try
            {
                Directory.Delete(targetUnc, true);
                output("[OK] Cleanup completed");
            }
            catch (Exception ex)
            {
                output($"WARNING: Cleanup failed: {ex.Message}");
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Registers a one-shot task, starts it, tails its log back through the pipeline and
    /// reports the exit code via the sentinel. Only the principal differs by context.
    /// </summary>
    private static string BuildTaskScript(bool runAsUser, string deployArgs, string logPath)
    {
        // S-1-5-18 rather than the account name: the SID is locale-independent.
        string principal = runAsUser
            ? """
              $user = (Get-CimInstance Win32_ComputerSystem).UserName
              if (-not $user) { throw 'No user is logged on to the target computer' }
              $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive
              $contextLabel = $user
              """
            : """
              $principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
              $contextLabel = 'NT AUTHORITY\SYSTEM'
              """;

        return """
            $taskName = '__TASK_NAME__'
            $logPath = '__LOG_PATH__'
            $neverRan = __NEVER_RAN__
            Remove-Item -Path $logPath -Force -ErrorAction SilentlyContinue
            __PRINCIPAL__
            $action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c powershell.exe __DEPLOY_ARGS__ > "__LOG_PATH__" 2>&1'
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
            Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal | Out-Null
            Start-ScheduledTask -TaskName $taskName
            "Deployment task started as $contextLabel"
            $offset = 0
            $elapsed = 0
            while ($elapsed -lt 3600) {
                Start-Sleep -Seconds 2
                $elapsed += 2
                if (Test-Path $logPath) {
                    $content = Get-Content -Path $logPath -Raw -ErrorAction SilentlyContinue
                    if ($content -and $content.Length -gt $offset) {
                        $content.Substring($offset)
                        $offset = $content.Length
                    }
                }
                $state = (Get-ScheduledTask -TaskName $taskName).State
                $result = (Get-ScheduledTaskInfo -TaskName $taskName).LastTaskResult
                if ($state -eq 'Ready' -and $result -ne $neverRan) { break }
                if ($state -ne 'Running' -and $result -eq $neverRan -and $elapsed -ge 120) { break }
            }
            $exitCode = (Get-ScheduledTaskInfo -TaskName $taskName).LastTaskResult
            Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
            if ($elapsed -ge 3600) {
                "ERROR: Deployment timed out after 60 minutes"
                $exitCode = -3
            }
            elseif ($exitCode -eq $neverRan) {
                "ERROR: The deployment task never started on the target"
                $exitCode = -2
            }
            "__SENTINEL__$exitCode"
            """
            .Replace("__PRINCIPAL__", principal)
            .Replace("__DEPLOY_ARGS__", EscapeSingleQuoted(deployArgs))
            .Replace("__LOG_PATH__", EscapeSingleQuoted(logPath))
            .Replace("__TASK_NAME__", TaskName)
            .Replace("__NEVER_RAN__", NeverRan.ToString())
            .Replace("__SENTINEL__", ExitCodeSentinel);
    }

    /// <summary>Both values land inside single-quoted PowerShell strings.</summary>
    private static string EscapeSingleQuoted(string value) => PowerShellLiteral.SingleQuoted(value);

    /// <summary>
    /// The folder name reaches a UNC path, a task command line and a /MIR destination.
    /// /MIR deletes, so anything that could redirect is replaced rather than escaped.
    /// </summary>
    private static string SanitiseFolderName(string name)
    {
        var cleaned = new string(name
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '"' or '\'' or '%' or '$' or '`' ? '_' : c)
            .ToArray())
            .Trim().Trim('.');

        return string.IsNullOrEmpty(cleaned) ? "Package" : cleaned;
    }

    private static void CopyWithRobocopy(string source, string destination, Action<int?>? progress)
    {
        // /MIR mirrors, /MT:16 multithreaded, /J unbuffered, /NOOFFLOAD skips the ODX
        // attempt (unusable over SMB), /R:2 /W:5 replaces the 1M-retry defaults, and
        // /NP /NFL /NDL drops per-file output, which otherwise dominates the runtime.
        var psi = new ProcessStartInfo
        {
            FileName = "robocopy.exe",
            Arguments = $"\"{source.TrimEnd('\\')}\" \"{destination.TrimEnd('\\')}\" /MIR /MT:16 /J /NOOFFLOAD /R:2 /W:5 /NP /NFL /NDL /NJH",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start robocopy.exe");
        var outputTask = process.StandardOutput.ReadToEndAsync();

        // Per-file output is off, so poll destination size against source size instead.
        if (progress != null)
        {
            long totalBytes = GetDirectorySize(source);
            while (!process.WaitForExit(5000))
            {
                if (totalBytes > 0)
                {
                    long copied = GetDirectorySize(destination);
                    progress((int)Math.Min(100, copied * 100 / totalBytes));
                }
            }
            progress(100);
        }

        process.WaitForExit();
        string result = outputTask.GetAwaiter().GetResult();

        // Robocopy exit codes are a bitfield: 0-7 succeeded, 8+ failed.
        if (process.ExitCode >= 8)
            throw new IOException($"Robocopy failed with exit code {process.ExitCode}:\n{result}");
    }

    // Tolerates files appearing and disappearing mid-copy.
    private static long GetDirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch
        {
            return 0;
        }
    }
}
