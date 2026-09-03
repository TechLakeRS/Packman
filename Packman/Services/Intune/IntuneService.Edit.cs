using Packman.Helpers;
using Packman.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Packman.Services;

public partial class IntuneService
{
    // ── Detection rules ─────────────────────────────────
    // No per-rule endpoint: the whole detectionRules array is PATCHed.
    public async Task UpdateDetectionRulesAsync(string appId, IEnumerable<DetectionRule> rules, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobApp",
            ["detectionRules"] = rules.Select(DetectionRuleGraph.Serialize).ToList(),
        };
        await _graph.PatchAsync($"{Base}/{appId}", payload, "Update detection rules", ct);
    }

    // ── Assignments ─────────────────────────────────────
    public async Task AddAssignmentAsync(string appId, string groupId, string intent, CancellationToken ct = default)
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
        await _graph.PostAsync($"{Base}/{appId}/assignments", payload, "Add assignment", ct);
    }

    public Task RemoveAssignmentAsync(string appId, string assignmentId, CancellationToken ct = default)
        => _graph.DeleteAsync($"{Base}/{appId}/assignments/{assignmentId}", "Remove assignment", ct);

    // ── Group members ───────────────────────────────────
    /// <summary>Every direct member of a group, all pages.</summary>
    public async Task<List<GroupMember>> GetGroupMembersAsync(string groupId, CancellationToken ct = default)
    {
        var members = new List<GroupMember>();
        var url = $"{GraphClient.Groups}/{groupId}/members?$select=id,displayName&$top=999";
        await foreach (var m in _graph.GetAllPagesAsync(url, "Load members", ct))
        {
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
        }
        return members;
    }

    /// <summary>
    /// Adds a device or user to a group. Returns false when it was already a member,
    /// which Graph reports as a 400 "already exist". Needs GroupMember.ReadWrite.All.
    /// </summary>
    public async Task<bool> AddGroupMemberAsync(string groupId, string directoryObjectId, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, string>
        {
            ["@odata.id"] = $"https://graph.microsoft.com/v1.0/directoryObjects/{directoryObjectId}",
        };
        var response = await _graph.PostAsync($"{GraphClient.Groups}/{groupId}/members/$ref", payload, "Add member", ct, throwOnError: false);
        if (response.IsSuccess) return true;
        if (response.StatusCode == 400 && response.Body.Contains("already exist", StringComparison.OrdinalIgnoreCase)) return false;
        throw response.ToException("Add member");
    }

    public Task RemoveGroupMemberAsync(string groupId, string memberId, CancellationToken ct = default)
        => _graph.DeleteAsync($"{GraphClient.Groups}/{groupId}/members/{memberId}/$ref", "Remove member", ct);

    /// <summary>
    /// Searches devices and users by name prefix for the add-member picker. Queried
    /// independently so a missing scope on one doesn't hide the other's results.
    /// </summary>
    public async Task<List<GroupMember>> SearchDevicesAndUsersAsync(string query, CancellationToken ct = default)
    {
        var results = new List<GroupMember>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        var filter = Uri.EscapeDataString($"startswith(displayName,'{OData.Literal(query.Trim())}')");
        results.AddRange(await SearchDirectoryAsync($"{GraphClient.Devices}?$filter={filter}&$select=id,displayName&$top=8", "Device", ct));
        results.AddRange(await SearchDirectoryAsync($"{GraphClient.Users}?$filter={filter}&$select=id,displayName&$top=8", "User", ct));
        return results;
    }

    private async Task<List<GroupMember>> SearchDirectoryAsync(string url, string kind, CancellationToken ct)
    {
        var results = new List<GroupMember>();
        var response = await _graph.GetAsync(url, $"Search {kind.ToLowerInvariant()}s", ct, throwOnError: false);
        if (!response.IsSuccess)
        {
            // Typically a missing directory scope; the other query still answers.
            Debug.WriteLine($"{kind} search failed: HTTP {response.StatusCode} {GraphException.ExtractMessage(response.Body)}");
            return results;
        }

        if (response.Json.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var m in arr.EnumerateArray())
                results.Add(new GroupMember
                {
                    Id = m.GetSafeString("id"),
                    DisplayName = m.GetSafeString("displayName"),
                    Kind = kind,
                });
        return results;
    }
}
