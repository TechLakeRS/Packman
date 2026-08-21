namespace Packman.Helpers;

/// <summary>Fixed file names in a PSADT v4 package. v4 only.</summary>
public static class PsadtLayout
{
    /// <summary>Entry point Intune launches; sent as setupFilePath.</summary>
    public const string SetupFileName = "Invoke-AppDeployToolkit.exe";

    /// <summary>Deployment script holding the metadata and install logic.</summary>
    public const string ScriptName = "Invoke-AppDeployToolkit.ps1";
}
