using System.Text.Json.Serialization;

namespace Packman.Models;

/// <summary>
/// Optional Intune Win32 app requirement rules. When the user leaves a field
/// empty the default (no constraint) is used; a set value overrides it.
/// </summary>
public class RequirementInfo
{
    /// <summary>Minimum OS values offered in the UI.</summary>
    public static readonly string[] SupportedOperatingSystems =
    {
        "Windows 10 1607", "Windows 10 1809", "Windows 10 1903", "Windows 10 2004",
        "Windows 10 21H2", "Windows 10 22H2", "Windows 11 21H2", "Windows 11 22H2"
    };

    public string MinimumOperatingSystem { get; set; } = "Windows 10 1607";
    public int? MinimumFreeDiskSpaceMB { get; set; }
    public int? MinimumMemoryMB { get; set; }
    public int? MinimumNumberOfProcessors { get; set; }
    public int? MinimumCpuSpeedMHz { get; set; }

    /// <summary>Builds requirements from the text fields; blank or non-positive means no constraint.</summary>
    public static RequirementInfo Parse(string operatingSystem, string freeDiskSpaceMB, string memoryMB, string processors, string cpuSpeedMHz) => new()
    {
        MinimumOperatingSystem = string.IsNullOrWhiteSpace(operatingSystem) ? SupportedOperatingSystems[0] : operatingSystem.Trim(),
        MinimumFreeDiskSpaceMB = Optional(freeDiskSpaceMB),
        MinimumMemoryMB = Optional(memoryMB),
        MinimumNumberOfProcessors = Optional(processors),
        MinimumCpuSpeedMHz = Optional(cpuSpeedMHz),
    };

    private static int? Optional(string? text)
        => int.TryParse(text?.Trim(), out var value) && value > 0 ? value : null;

    /// <summary>
    /// Maps the saved friendly OS name to Graph's minimumSupportedWindowsRelease value.
    /// The older boolean OS object cannot distinguish recent Windows 10 and 11 releases.
    /// </summary>
    [JsonIgnore]
    public string MinimumSupportedWindowsRelease => MinimumOperatingSystem switch
    {
        "Windows 10 1809" => "1809",
        "Windows 10 1903" => "1903",
        "Windows 10 2004" => "2004",
        "Windows 10 21H2" => "Windows10_21H2",
        "Windows 10 22H2" => "Windows10_22H2",
        "Windows 11 21H2" => "Windows11_21H2",
        "Windows 11 22H2" => "Windows11_22H2",
        _ => "1607"
    };

    public static string FormatWindowsRelease(string release)
    {
        if (string.IsNullOrWhiteSpace(release)) return "Not specified";
        if (release.StartsWith("Windows11_", StringComparison.OrdinalIgnoreCase))
            return "Windows 11 " + release[10..];
        if (release.StartsWith("Windows10_", StringComparison.OrdinalIgnoreCase))
            return "Windows 10 " + release[10..];
        return "Windows 10 " + (release == "2H20" ? "20H2" : release);
    }
}
