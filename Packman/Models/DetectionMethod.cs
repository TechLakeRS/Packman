namespace Packman.Models;

/// <summary>The detection methods Packman can build a Win32 detection rule from.</summary>
public static class DetectionMethod
{
    public const string FileExists = "File exists";
    public const string FileVersion = "File version";
    public const string RegistryKey = "Registry key exists";
    public const string MsiProductCode = "MSI product code";

    public static List<string> All => new() { FileExists, FileVersion, RegistryKey, MsiProductCode };
}

/// <summary>Registry root keys accepted by the Intune registry detection rule.</summary>
public static class RegistryHiveNames
{
    public const string LocalMachine = "HKEY_LOCAL_MACHINE";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        LocalMachine,
        "HKEY_CURRENT_USER",
        "HKEY_CLASSES_ROOT",
        "HKEY_USERS",
        "HKEY_CURRENT_CONFIG",
    };

    /// <summary>Joins a hive and a sub-key into the keyPath Graph expects.</summary>
    public static string Combine(string hive, string subKey)
    {
        var key = subKey.Trim().Trim('\\');
        return string.IsNullOrEmpty(key) ? hive : $"{hive}\\{key}";
    }
}
