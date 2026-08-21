using Packman.Helpers;
using Packman.Models;
using System.Diagnostics;
using System.IO;

namespace Packman.Services;

public class PSADTGenerator
{
    private readonly string _baseOutputPath;
    private readonly string _templatePath;

    public PSADTGenerator(string baseOutputPath, string templatePath)
    {
        _baseOutputPath = baseOutputPath;
        _templatePath = templatePath;
    }

    public PackageValidationResult ValidatePackageCreation(ApplicationInfo appInfo)
    {
        var appFolderName = $"{appInfo.Manufacturer.Replace(" ", "_")}_{appInfo.Name.Replace(" ", "_")}";
        var packagePath = Path.Combine(_baseOutputPath, appFolderName, appInfo.Version);
        return new PackageValidationResult
        {
            PackageExists = Directory.Exists(packagePath),
            ExistingPath = packagePath,
            ProposedPath = packagePath,
            AppFolderName = appFolderName,
            Version = appInfo.Version
        };
    }

    public async Task<string> CreatePackageAsync(ApplicationInfo appInfo,
        bool overwriteExisting = false, CancellationToken cancellationToken = default)
    {
        var appFolderName = $"{appInfo.Manufacturer.Replace(" ", "_")}_{appInfo.Name.Replace(" ", "_")}";
        var packagePath = Path.Combine(_baseOutputPath, appFolderName, appInfo.Version);

        if (Directory.Exists(packagePath))
        {
            if (!overwriteExisting)
                throw new InvalidOperationException(
                    $"Package version {appInfo.Version} already exists for {appFolderName}.");
            Directory.Delete(packagePath, true);
            await Task.Delay(200, cancellationToken);
        }

        Directory.CreateDirectory(Path.Combine(_baseOutputPath, appFolderName));
        await CopyTemplateFolderAsync(packagePath, cancellationToken);

        if (!string.IsNullOrWhiteSpace(appInfo.SourcesPath))
            await CopySourceFilesAsync(appInfo.SourcesPath, packagePath, cancellationToken);

        await ModifyScriptAsync(packagePath, appInfo, cancellationToken);

        Debug.WriteLine($"Package created at: {packagePath}");
        return packagePath;
    }

    // Accepts either the .ps1 path or the folder holding it.
    private string ResolveTemplatePath()
    {
        var p = _templatePath?.Trim();
        if (!string.IsNullOrEmpty(p))
        {
            if (p.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                return Path.GetDirectoryName(p)!;
            if (Directory.Exists(p) && File.Exists(Path.Combine(p, "Invoke-AppDeployToolkit.ps1")))
                return p;
        }

        throw new DirectoryNotFoundException(
            $"PSADT template not found. Searched: '{_templatePath}'. " +
            "Set the PSADT Template Path in Settings > Network Paths to the PSADT folder containing Invoke-AppDeployToolkit.ps1.");
    }

    private async Task CopyTemplateFolderAsync(string packagePath, CancellationToken ct)
    {
        var template = ResolveTemplatePath();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(packagePath);
            CopyDir(template, Path.Combine(packagePath, "Application"));
            // Created up front so new and upgraded packages share one layout.
            Directory.CreateDirectory(Path.Combine(packagePath, "Intune"));
            Directory.CreateDirectory(Path.Combine(packagePath, "Icon"));
        }, ct);
    }

    private async Task CopySourceFilesAsync(string sourcesPath, string packagePath, CancellationToken ct)
    {
        var dest = Path.Combine(packagePath, "Application", "Files");
        Directory.CreateDirectory(dest);
        await Task.Run(() =>
        {
            if (sourcesPath.Contains(';'))
            {
                foreach (var p in sourcesPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = p.Trim();
                    if (File.Exists(t)) File.Copy(t, Path.Combine(dest, Path.GetFileName(t)), true);
                }
            }
            else if (File.Exists(sourcesPath))
                File.Copy(sourcesPath, Path.Combine(dest, Path.GetFileName(sourcesPath)), true);
            else if (Directory.Exists(sourcesPath))
                CopyDir(sourcesPath, dest);
        }, ct);
    }

    private async Task ModifyScriptAsync(string packagePath, ApplicationInfo appInfo, CancellationToken ct)
    {
        var scriptPath = Path.Combine(packagePath, "Application", "Invoke-AppDeployToolkit.ps1");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"Invoke-AppDeployToolkit.ps1 not found in: {packagePath}");

        var content = await File.ReadAllTextAsync(scriptPath, ct);
        content = UpdateMetadata(content, appInfo);
        content = InjectInstallCommands(content, appInfo);
        await File.WriteAllTextAsync(scriptPath, content, ct);
    }

    private string UpdateMetadata(string content, ApplicationInfo appInfo)
    {
        var lines = content.Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("AppVendor") && line.Contains("="))
                lines[i] = $"    AppVendor = '{Q(appInfo.Manufacturer)}'";
            else if (line.StartsWith("AppName") && line.Contains("=") && !line.Contains("AppNameWithVersion"))
                lines[i] = $"    AppName = '{Q(appInfo.Name)}'";
            else if (line.StartsWith("AppVersion") && line.Contains("="))
                lines[i] = $"    AppVersion = '{Q(appInfo.Version)}'";
            else if (line.StartsWith("AppArch") && line.Contains("="))
                lines[i] = $"    AppArch = '{Q(appInfo.Architecture)}'";
            else if (line.StartsWith("AppScriptDate") && line.Contains("="))
                lines[i] = $"    AppScriptDate = '{DateTime.Now:MM/dd/yyyy}'";
            else if (line.StartsWith("AppScriptAuthor") && line.Contains("="))
                lines[i] = $"    AppScriptAuthor = '{Q(string.IsNullOrWhiteSpace(appInfo.Author) ? Environment.UserName : appInfo.Author)}'";
            else if (line.StartsWith("RequireAdmin") && line.Contains("="))
                lines[i] = $"    RequireAdmin = ${(appInfo.InstallContext.Equals("User", StringComparison.OrdinalIgnoreCase) ? "false" : "true")}";
        }
        return string.Join('\n', lines);
    }

    private string InjectInstallCommands(string content, ApplicationInfo appInfo)
    {
        var lines = content.Split('\n').ToList();
        var sourceFileName = !string.IsNullOrEmpty(appInfo.SourcesPath)
            ? Path.GetFileName(appInfo.SourcesPath) : null;

        int installIdx = FindSection(lines, "Installation");
        if (installIdx > 0)
        {
            string code = appInfo.PackageType == "MSI"
                ? $"\n## MSI Installation\nStart-ADTMsiProcess -Action 'Install' -FilePath \"$($adtSession.DirFiles)\\{D(sourceFileName ?? appInfo.Name + ".msi")}\""
                : $"\n## EXE Installation\nStart-ADTProcess -FilePath \"$($adtSession.DirFiles)\\{D(sourceFileName ?? "setup.exe")}\" -ArgumentList '<silent flags>'";
            Insert(lines, installIdx, code);
        }

        int uninstallIdx = FindSection(lines, "Uninstallation");
        if (uninstallIdx > 0)
        {
            string code = appInfo.PackageType == "MSI"
                ? $"\n## Uninstall MSI\nStart-ADTMsiProcess -Action 'Uninstall' -FilePath '{Q(string.IsNullOrEmpty(appInfo.MsiProductCode) ? "{ProductCode}" : appInfo.MsiProductCode)}'"
                : $"\n## Uninstall EXE\nStart-ADTProcess -FilePath \"$($adtSession.DirFiles)\\{D(sourceFileName ?? "setup.exe")}\" -ArgumentList '<uninstall flags>'";
            Insert(lines, uninstallIdx, code);
        }

        return string.Join('\n', lines);
    }

    private static string Q(string? value) => PowerShellLiteral.SingleQuoted(value);
    private static string D(string? value) => PowerShellLiteral.DoubleQuoted(value);

    private int FindSection(List<string> lines, string name)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains($"<Perform {name} tasks here>") ||
                lines[i].Contains($"## {name}") ||
                lines[i].Contains($"## <{name}>"))
                return i + 1;
        }
        return -1;
    }

    private void Insert(List<string> lines, int index, string code)
    {
        var codeLines = code.Split('\n');
        for (int i = 0; i < codeLines.Length; i++)
            lines.Insert(index + i, codeLines[i]);
    }

    private void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
