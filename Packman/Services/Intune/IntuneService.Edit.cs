using Packman.Helpers;
using Packman.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Packman.Services;

public partial class IntuneService
{
    private const string GraphBeta = "https://graph.microsoft.com/beta";

    // ── Detection rules ─────────────────────────────────
    // No per-rule endpoint: the whole detectionRules array is PATCHed.
    public async Task UpdateDetectionRulesAsync(string appId, IEnumerable<DetectionRule> rules)
    {
        var payload = new Dictionary<string, object?>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobApp",
            ["detectionRules"] = rules.Select(SerializeDetectionRule).ToList(),
        };
        using var request = await AuthRequestAsync(HttpMethod.Patch, $"{Base}/{appId}");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Could not update detection rules ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    }

    private static Dictionary<string, object?> SerializeDetectionRule(DetectionRule r) => r.Type switch
    {
        DetectionRuleType.MSI => new()
        {
            ["@odata.type"] = "#microsoft.graph.win32LobAppProductCodeDetection",
            ["productCode"] = r.Path,
            ["productVersion"] = r.CheckVersion ? r.FileOrFolderName : null,
            ["productVersionOperator"] = r.CheckVersion ? DefaultOperator(r.Operator) : "notConfigured",
        },
        DetectionRuleType.File => new()
        {
            ["@odata.type"] = "#microsoft.graph.win32LobAppFileSystemDetection",
            ["path"] = r.Path,
            ["fileOrFolderName"] = r.FileOrFolderName,
            ["check32BitOn64System"] = r.Check32BitOn64System,
            ["detectionType"] = string.IsNullOrEmpty(r.DetectionType) ? "exists" : r.DetectionType,
            ["operator"] = OperatorNeedsValue(r.DetectionType) ? DefaultOperator(r.Operator) : "notConfigured",
            ["detectionValue"] = OperatorNeedsValue(r.DetectionType) ? r.DetectionValue : null,
        },
        DetectionRuleType.Registry => new()
        {
            ["@odata.type"] = "#microsoft.graph.win32LobAppRegistryDetection",
            ["keyPath"] = r.Path,
            ["valueName"] = r.FileOrFolderName,
            ["check32BitOn64System"] = r.Check32BitOn64System,
            ["detectionType"] = string.IsNullOrEmpty(r.DetectionType) ? "exists" : r.DetectionType,
            ["operator"] = OperatorNeedsValue(r.DetectionType) ? DefaultOperator(r.Operator) : "notConfigured",
            ["detectionValue"] = OperatorNeedsValue(r.DetectionType) ? r.DetectionValue : null,
        },
        DetectionRuleType.Script => new()
        {
            ["@odata.type"] = "#microsoft.graph.win32LobAppPowerShellScriptDetection",
            ["scriptContent"] = r.ScriptContent,
            ["enforceSignatureCheck"] = r.EnforceSignatureCheck,
            ["runAs32Bit"] = r.RunAs32Bit,
        },
        _ => throw new NotSupportedException($"Unknown detection rule type: {r.Type}"),
    };

    private static bool OperatorNeedsValue(string detectionType) =>
        detectionType is "version" or "string" or "integer" or "sizeInMB" or "modifiedDate";

    private static string DefaultOperator(string op) =>
        string.IsNullOrEmpty(op) || op == "notConfigured" ? "equal" : op;

    // ── Assignments ─────────────────────────────────────
    public async Task AddAssignmentAsync(string appId, string groupId, string intent)
    {
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.mobileAppAssignment",
            ["intent"] = intent,
            ["target"] = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget",
                ["groupId"] = groupId,
            },
        };
        using var request = await AuthRequestAsync(HttpMethod.Post, $"{Base}/{appId}/assignments");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Could not add assignment ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    }

    public async Task RemoveAssignmentAsync(string appId, string assignmentId)
    {
        using var request = await AuthRequestAsync(HttpMethod.Delete, $"{Base}/{appId}/assignments/{assignmentId}");
        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Could not remove assignment ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    }

    // ── Group members ───────────────────────────────────
    public async Task<List<GroupMember>> GetGroupMembersAsync(string groupId)
    {
        var members = new List<GroupMember>();
        var url = $"{GraphBeta}/groups/{groupId}/members?$select=id,displayName&$top=100";
        using var request = await AuthRequestAsync(HttpMethod.Get, url);
        var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Could not load members ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var m in arr.EnumerateArray())
                members.Add(new GroupMember
                {
                    Id = m.GetSafeString("id"),
                    DisplayName = m.GetSafeString("displayName"),
                    Kind = m.GetSafeString("@odata.type") switch
                    {
                        "#microsoft.graph.device" => "Device",
                        "#microsoft.graph.user" => "User",
                        "#microsoft.graph.group" => "Group",
                        var t => t.Replace("#microsoft.graph.", ""),
                    },
                });
        return members;
    }

    /// <summary>Adds a device or user to a group. Needs GroupMember.ReadWrite.All.</summary>
    public async Task AddGroupMemberAsync(string groupId, string directoryObjectId)
    {
        var payload = new Dictionary<string, string>
        {
            ["@odata.id"] = $"https://graph.microsoft.com/v1.0/directoryObjects/{directoryObjectId}",
        };
        using var request = await AuthRequestAsync(HttpMethod.Post, $"{GraphBeta}/groups/{groupId}/members/$ref");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Could not add member ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    }

    public async Task RemoveGroupMemberAsync(string groupId, string memberId)
    {
        using var request = await AuthRequestAsync(HttpMethod.Delete, $"{GraphBeta}/groups/{groupId}/members/{memberId}/$ref");
        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Could not remove member ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Searches devices and users by name prefix for the add-member picker. Queried
    /// independently so a missing scope on one doesn't hide the other's results.
    /// </summary>
    public async Task<List<GroupMember>> SearchDevicesAndUsersAsync(string query)
    {
        var results = new List<GroupMember>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        var filter = Uri.EscapeDataString($"startswith(displayName,'{OData.Literal(query.Trim())}')");
        results.AddRange(await SearchDirectoryAsync($"{GraphBeta}/devices?$filter={filter}&$select=id,displayName&$top=8", "Device"));
        results.AddRange(await SearchDirectoryAsync($"{GraphBeta}/users?$filter={filter}&$select=id,displayName&$top=8", "User"));
        return results;
    }

    private async Task<List<GroupMember>> SearchDirectoryAsync(string url, string kind)
    {
        var results = new List<GroupMember>();
        try
        {
            using var request = await AuthRequestAsync(HttpMethod.Get, url);
            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return results;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var m in arr.EnumerateArray())
                    results.Add(new GroupMember
                    {
                        Id = m.GetSafeString("id"),
                        DisplayName = m.GetSafeString("displayName"),
                        Kind = kind,
                    });
        }
        catch
        {
            // Missing directory scope; return what the other query found.
        }
        return results;
    }
}
