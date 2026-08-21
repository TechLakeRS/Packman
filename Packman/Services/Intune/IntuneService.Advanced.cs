using Packman.Helpers;
using Packman.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Packman.Services;

/// <summary>
/// Directory lookups the Intune portal doesn't offer: bulk PC name resolution, a device's
/// groups, and every app targeting a group. Graph has no reverse index for the last one.
/// </summary>
public partial class IntuneService
{
    /// <summary>
    /// Resolves PC names to Entra devices, in chunks to stay under the Graph filter length.
    /// Stale re-enrolments mean one name can match several records, so all are returned.
    /// </summary>
    public async Task<Dictionary<string, List<EntraDevice>>> FindDevicesByNamesAsync(IReadOnlyList<string> names)
    {
        var byName = new Dictionary<string, List<EntraDevice>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names) byName[name] = new List<EntraDevice>();

        foreach (var chunk in names.Chunk(15))
        {
            var clauses = chunk.Select(n => $"displayName eq '{OData.Literal(n)}'");
            var filter = Uri.EscapeDataString(string.Join(" or ", clauses));
            var url = $"{GraphBeta}/devices?$filter={filter}&$select={DeviceSelect}&$top=999";

            foreach (var device in await ReadDevicesAsync(url, allPages: true))
                if (byName.TryGetValue(device.DisplayName, out var matches))
                    matches.Add(device);
        }
        return byName;
    }

    /// <summary>Searches devices by name prefix, for the PC lookup box.</summary>
    public async Task<List<EntraDevice>> SearchDevicesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<EntraDevice>();
        var filter = Uri.EscapeDataString($"startswith(displayName,'{OData.Literal(query.Trim())}')");
        return await ReadDevicesAsync($"{GraphBeta}/devices?$filter={filter}&$select={DeviceSelect}&$top=10", allPages: false);
    }

    /// <summary>
    /// Adds a directory object to a group; false when it was already a member. Graph
    /// reports that as a 400 "already exist", which a bulk run skips rather than fails.
    /// </summary>
    public async Task<bool> TryAddGroupMemberAsync(string groupId, string directoryObjectId)
    {
        var payload = new Dictionary<string, string>
        {
            ["@odata.id"] = $"https://graph.microsoft.com/v1.0/directoryObjects/{directoryObjectId}",
        };
        using var request = await AuthRequestAsync(HttpMethod.Post, $"{GraphBeta}/groups/{groupId}/members/$ref");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Http.SendAsync(request);
        if (response.IsSuccessStatusCode) return true;

        var body = await response.Content.ReadAsStringAsync();
        if (body.Contains("already exist", StringComparison.OrdinalIgnoreCase)) return false;
        throw new Exception($"({(int)response.StatusCode}) {body}");
    }

    /// <summary>Lists the groups a device is a direct member of.</summary>
    public async Task<List<DeviceGroupMembership>> GetDeviceGroupsAsync(string deviceObjectId)
    {
        var groups = new List<DeviceGroupMembership>();
        var url = $"{GraphBeta}/devices/{deviceObjectId}/memberOf?$top=100";

        while (!string.IsNullOrEmpty(url))
        {
            using var request = await AuthRequestAsync(HttpMethod.Get, url);
            var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Could not read group membership ({(int)response.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var g in arr.EnumerateArray())
                {
                    // memberOf also returns directory roles.
                    if (g.GetSafeString("@odata.type") != "#microsoft.graph.group") continue;
                    groups.Add(new DeviceGroupMembership
                    {
                        Id = g.GetSafeString("id"),
                        DisplayName = g.GetSafeString("displayName"),
                        Description = g.GetSafeString("description"),
                        MembershipType = IsDynamic(g) ? "Dynamic" : "Assigned",
                    });
                }

            url = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() ?? "" : "";
        }
        return groups.OrderBy(g => g.DisplayName).ToList();
    }

    /// <summary>
    /// Finds every app assigned to a group. Graph only answers the reverse question, so the
    /// app list is walked with assignments expanded and filtered here.
    /// <paramref name="progress"/> reports apps scanned so far.
    /// </summary>
    public async Task<List<GroupAppAssignment>> GetGroupAppAssignmentsAsync(string groupId, IProgress<int>? progress = null)
    {
        var matches = new List<GroupAppAssignment>();
        var scanned = 0;
        var url = $"{Base}?$expand=assignments&$top=50";

        while (!string.IsNullOrEmpty(url))
        {
            using var request = await AuthRequestAsync(HttpMethod.Get, url);
            var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Could not scan app assignments ({(int)response.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("value", out var apps) && apps.ValueKind == JsonValueKind.Array)
                foreach (var app in apps.EnumerateArray())
                {
                    scanned++;
                    if (!app.TryGetProperty("assignments", out var assignments) || assignments.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var a in assignments.EnumerateArray())
                    {
                        if (!a.TryGetProperty("target", out var target)) continue;
                        if (!string.Equals(target.GetSafeString("groupId"), groupId, StringComparison.OrdinalIgnoreCase)) continue;

                        matches.Add(new GroupAppAssignment
                        {
                            AppId = app.GetSafeString("id"),
                            DisplayName = app.GetSafeString("displayName"),
                            Publisher = app.GetSafeString("publisher"),
                            AppType = FriendlyAppType(app.GetSafeString("@odata.type")),
                            Intent = a.GetSafeString("intent"),
                            IsExcluded = target.GetSafeString("@odata.type")
                                .Contains("exclusionGroupAssignmentTarget", StringComparison.OrdinalIgnoreCase),
                        });
                    }
                }

            progress?.Report(scanned);
            url = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() ?? "" : "";
        }
        return matches.OrderBy(m => m.DisplayName).ToList();
    }

    private const string DeviceSelect = "id,displayName,operatingSystem,operatingSystemVersion,accountEnabled";

    private async Task<List<EntraDevice>> ReadDevicesAsync(string url, bool allPages)
    {
        var devices = new List<EntraDevice>();
        while (!string.IsNullOrEmpty(url))
        {
            using var request = await AuthRequestAsync(HttpMethod.Get, url);
            var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Device lookup failed ({(int)response.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var d in arr.EnumerateArray())
                    devices.Add(new EntraDevice
                    {
                        Id = d.GetSafeString("id"),
                        DisplayName = d.GetSafeString("displayName"),
                        OperatingSystem = d.GetSafeString("operatingSystem"),
                        OperatingSystemVersion = d.GetSafeString("operatingSystemVersion"),
                        Enabled = !d.TryGetProperty("accountEnabled", out var ae) || ae.ValueKind != JsonValueKind.False,
                    });

            url = allPages && root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() ?? "" : "";
        }
        return devices;
    }

    private static bool IsDynamic(JsonElement group)
        => group.TryGetProperty("groupTypes", out var types) && types.ValueKind == JsonValueKind.Array
           && types.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String
               && string.Equals(t.GetString(), "DynamicMembership", StringComparison.OrdinalIgnoreCase));

    private static string FriendlyAppType(string odataType) => odataType.Replace("#microsoft.graph.", "") switch
    {
        "win32LobApp" => "Win32",
        "winGetApp" => "WinGet",
        "windowsMobileMSI" => "MSI",
        "windowsUniversalAppX" => "UWP",
        "windowsStoreApp" => "Microsoft Store",
        "officeSuiteApp" => "Microsoft 365 Apps",
        "windowsWebApp" or "webApp" => "Web link",
        "" => "App",
        var other => other,
    };
}
