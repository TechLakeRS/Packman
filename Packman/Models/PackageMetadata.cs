namespace Packman.Models;

/// <summary>
/// Metadata describing a PSADT package, extracted from the deploy script.
/// Used to auto-fill the upgrade and upload forms.
/// </summary>
public class PackageMetadata
{
    public string Manufacturer { get; set; } = "";
    public string AppName { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallContext { get; set; } = "System";
    public string IconFileName { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public string PackageType { get; set; } = "";
}
