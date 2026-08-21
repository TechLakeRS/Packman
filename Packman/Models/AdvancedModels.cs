namespace Packman.Models;

/// <summary>An Entra (Azure AD) device record, resolved from a PC name.</summary>
public class EntraDevice
{
    /// <summary>Directory object id; group membership is written against this.</summary>
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string OperatingSystemVersion { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public string Subtitle
    {
        get
        {
            var os = string.Join(" ", new[] { OperatingSystem, OperatingSystemVersion }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrEmpty(os)) os = "Unknown OS";
            return Enabled ? os : $"{os} · disabled";
        }
    }
}

/// <summary>One PC name in a bulk add-to-group run, with the outcome of its lookup and add.</summary>
public class BulkAddRow
{
    public const string StatusAdded = "Added";
    public const string StatusAlready = "Already a member";
    public const string StatusNotFound = "Not found";
    public const string StatusFailed = "Failed";

    public string PcName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Detail { get; set; } = "";
}

/// <summary>A group a device belongs to, as returned by the PC → groups lookup.</summary>
public class DeviceGroupMembership
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Assigned or Dynamic. Dynamic groups cannot be edited by hand.</summary>
    public string MembershipType { get; set; } = "";
}

/// <summary>An app that targets a given group, as returned by the group → apps lookup.</summary>
public class GroupAppAssignment
{
    public string AppId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string AppType { get; set; } = "";
    public string Intent { get; set; } = "";
    public bool IsExcluded { get; set; }

    public string IntentText
    {
        get
        {
            var intent = string.IsNullOrEmpty(Intent) ? "Unknown" : char.ToUpperInvariant(Intent[0]) + Intent[1..];
            return IsExcluded ? $"Excluded · {intent}" : intent;
        }
    }
}
