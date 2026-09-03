using Packman.Helpers;
using Packman.Models;

namespace Packman.Services;

public partial class IntuneUploadService
{
    /// <summary>
    /// Assigns the published app to the groups picked for this upload plus the ones set on
    /// the Settings page. Failures are logged as warnings and never fail the upload.
    /// </summary>
    private async Task AssignGroupsAsync(string appId, ApplicationInfo appInfo, AppSettings.GroupAssignmentConfig config,
                                         IEnumerable<AssignedGroup>? pickedGroups, UploadLogger log, CancellationToken ct)
    {
        foreach (var picked in pickedGroups ?? Enumerable.Empty<AssignedGroup>())
        {
            if (string.IsNullOrWhiteSpace(picked.GroupId)) continue;
            await CreateGroupAssignmentAsync(appId, picked.GroupId, ParseIntent(picked.AssignmentType), picked.GroupName, log, ct);
        }

        foreach (var existing in config.ExistingGroups)
        {
            if (string.IsNullOrWhiteSpace(existing.GroupName)) continue;
            var groupId = await ResolveGroupIdAsync(existing.GroupName, log, ct);
            if (groupId == null)
            {
                log.Warning($"Group '{existing.GroupName}' not found in Entra ID - skipped");
                continue;
            }
            await CreateGroupAssignmentAsync(appId, groupId, existing.Intent, existing.GroupName, log, ct);
        }

        if (config.CreateGroupPerPackage)
            await AssignPerPackageGroupAsync(appId, appInfo, config.GroupNameTemplate, config.NewGroupIntent, log, ct);

        if (config.CreateUninstallGroupPerPackage)
            await AssignPerPackageGroupAsync(appId, appInfo, config.UninstallGroupNameTemplate, AssignmentIntent.Uninstall, log, ct);
    }

    /// <summary>Resolves or creates the per-package group and assigns it.</summary>
    private async Task AssignPerPackageGroupAsync(string appId, ApplicationInfo appInfo, string template, AssignmentIntent intent, UploadLogger log, CancellationToken ct)
    {
        var name = GroupAssignmentNamer.Build(template, appInfo.Manufacturer, appInfo.Name, appInfo.Version);
        if (string.IsNullOrWhiteSpace(name))
        {
            log.Warning($"Per-package group name for the {intent} assignment resolved to empty - skipped");
            return;
        }
        // Reuse an existing group with this name before creating one.
        var groupId = await ResolveGroupIdAsync(name, log, ct) ?? await CreateSecurityGroupAsync(name, log, ct);
        if (groupId != null)
            await CreateGroupAssignmentAsync(appId, groupId, intent, name, log, ct);
    }

    private async Task<string?> ResolveGroupIdAsync(string displayName, UploadLogger log, CancellationToken ct)
    {
        var filter = Uri.EscapeDataString($"displayName eq '{OData.Literal(displayName.Trim())}'");
        var response = await _graph.GetAsync($"{GraphClient.Groups}?$filter={filter}&$select=id", "Group lookup", ct, throwOnError: false);
        if (!response.IsSuccess)
        {
            log.Warning($"Could not look up group '{displayName}' (HTTP {response.StatusCode}): {GraphException.ExtractMessage(response.Body)}");
            return null;
        }

        var page = response.Json;
        if (page.TryGetProperty("value", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array && arr.GetArrayLength() > 0)
            return arr[0].GetSafeString("id") is { Length: > 0 } id ? id : null;
        return null;
    }

    private async Task<string?> CreateSecurityGroupAsync(string displayName, UploadLogger log, CancellationToken ct)
    {
        var payload = new
        {
            displayName,
            mailEnabled = false,
            mailNickname = SanitizeMailNickname(displayName),
            securityEnabled = true,
            groupTypes = Array.Empty<string>(),
        };
        var response = await _graph.PostAsync(GraphClient.Groups, payload, "Create group", ct, throwOnError: false);
        if (!response.IsSuccess)
        {
            var hint = response.StatusCode == 403 ? " (needs Group.ReadWrite.All; sign out and in again to consent)" : "";
            log.Warning($"Could not create group '{displayName}' (HTTP {response.StatusCode}){hint}: {GraphException.ExtractMessage(response.Body)}");
            return null;
        }

        var id = response.Json.GetSafeString("id");
        log.Success($"Created group '{displayName}'");
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private async Task CreateGroupAssignmentAsync(string appId, string groupId, AssignmentIntent intent, string groupName, UploadLogger log, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.mobileAppAssignment",
            ["intent"] = intent switch
            {
                AssignmentIntent.Required => "required",
                AssignmentIntent.Uninstall => "uninstall",
                _ => "available",
            },
            ["target"] = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget",
                ["groupId"] = groupId,
            },
        };
        var response = await _graph.PostAsync($"{GraphClient.MobileApps}/{appId}/assignments", payload, "Assign group", ct, throwOnError: false);
        if (response.IsSuccess)
            log.Success($"Assigned '{groupName}' ({intent})");
        else
            log.Warning($"Could not assign '{groupName}' (HTTP {response.StatusCode}): {GraphException.ExtractMessage(response.Body)}");
    }

    private static AssignmentIntent ParseIntent(string intent) => intent.ToLowerInvariant() switch
    {
        "uninstall" => AssignmentIntent.Uninstall,
        "available" => AssignmentIntent.Available,
        _ => AssignmentIntent.Required,
    };

    // Graph caps mailNickname at 64 characters and allows letters, digits, '_' and '-'.
    private static string SanitizeMailNickname(string displayName)
    {
        var nickname = new string(displayName.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').ToArray());
        if (nickname.Length > 64) nickname = nickname[..64];
        return string.IsNullOrEmpty(nickname) ? "group" : nickname;
    }
}
