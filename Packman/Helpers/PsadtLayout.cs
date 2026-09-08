namespace Packman.Helpers;

/// <summary>Fixed file names in a PSADT v4 package. v4 only.</summary>
public static class PsadtLayout
{
    /// <summary>Entry point Intune launches; sent as setupFilePath.</summary>
    public const string SetupFileName = "Invoke-AppDeployToolkit.exe";

    /// <summary>Deployment script holding the metadata and install logic.</summary>
    public const string ScriptName = "Invoke-AppDeployToolkit.ps1";

    /// <summary>PSADT's own default; appends no -DeployMode switch.</summary>
    public const string DeployModeDefault = "Auto";

    /// <summary>The -DeployMode values the frontend script accepts, plus Auto for "leave it to PSADT".</summary>
    public static readonly string[] DeployModes = ["Auto", "Interactive", "NonInteractive", "Silent"];

    /// <summary>
    /// Appends -DeployMode to a command line. Auto is the PSADT default so it is left off,
    /// and a command that already sets the switch is used as written.
    /// </summary>
    public static string WithDeployMode(string? command, string deployMode)
    {
        command = (command ?? "").Trim();
        if (deployMode == DeployModeDefault) return command;
        if (command.Contains("-DeployMode", StringComparison.OrdinalIgnoreCase)) return command;
        return $"{command} -DeployMode {deployMode}";
    }
}
