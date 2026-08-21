namespace Packman.Services;

public class PackageValidationResult
{
    public bool PackageExists { get; set; }
    public string ExistingPath { get; set; } = "";
    public string ProposedPath { get; set; } = "";
    public string AppFolderName { get; set; } = "";
    public string Version { get; set; } = "";
}

