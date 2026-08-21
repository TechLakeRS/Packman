using System.Text.Json.Serialization;

namespace Packman.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReturnCodeType { Success, SoftReboot, HardReboot, Retry, Failed }

/// <summary>
/// A single Intune Win32 app return code mapping (exit code → outcome).
/// </summary>
public class ReturnCodeInfo
{
    public int Code { get; set; }
    public ReturnCodeType Type { get; set; } = ReturnCodeType.Success;

    /// <summary>Free-text note shown next to the code in Packman. Graph has no field for it, so it is never uploaded.</summary>
    public string Description { get; set; } = "";

    /// <summary>The value Graph expects for win32LobAppReturnCode.type.</summary>
    [JsonIgnore]
    public string GraphType => Type switch
    {
        ReturnCodeType.SoftReboot => "softReboot",
        ReturnCodeType.HardReboot => "hardReboot",
        ReturnCodeType.Retry => "retry",
        ReturnCodeType.Failed => "failed",
        _ => "success"
    };

    /// <summary>The codes Intune applies when nothing else is configured.</summary>
    public static List<ReturnCodeInfo> Defaults() => new()
    {
        new() { Code = 0,    Type = ReturnCodeType.Success,    Description = "Completed successfully" },
        new() { Code = 3010, Type = ReturnCodeType.SoftReboot, Description = "Restart required to finish" },
        new() { Code = 1641, Type = ReturnCodeType.HardReboot, Description = "Installer initiated a restart" },
        new() { Code = 1618, Type = ReturnCodeType.Retry,      Description = "Another installation is in progress" }
    };
}
