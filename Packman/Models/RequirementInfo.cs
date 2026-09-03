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
    /// Maps the friendly OS name to the Graph minimumSupportedOperatingSystem flag.
    /// </summary>
    [JsonIgnore]
    public string OperatingSystemFlag => MinimumOperatingSystem switch
    {
        "Windows 10 1809" => "v10_1809",
        "Windows 10 1903" => "v10_1903",
        "Windows 10 2004" => "v10_2004",
        "Windows 10 21H2" => "v10_21H2",
        "Windows 10 22H2" => "v10_22H2",
        "Windows 11 21H2" => "v10_21H2",
        "Windows 11 22H2" => "v10_22H2",
        _ => "v10_1607"
    };
}
