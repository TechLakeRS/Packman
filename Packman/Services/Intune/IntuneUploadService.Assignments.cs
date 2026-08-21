using Packman.Helpers;
using Packman.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Packman.Services;

public partial class IntuneUploadService
{
    private const string GraphBeta = "https://graph.microsoft.com/beta";

    /// <summary>
    /// Assigns the published app to the groups picked for this upload plus the ones set on
    /// the Settings page. Failures are logged as warnings and never fail the upload.
    /// </summary>
    private async Task AssignGroupsAsync(string appId, ApplicationInfo appInfo, AppSettings.GroupAssignmentConfig config,
                                         IEnumerable<AssignedGroup>? pickedGroups, UploadLogger log)
    {
        foreach (var picked in pickedGroups ?? Enumerable.Empty<AssignedGroup>())
        {
            if (string.IsNullOrWhiteSpace(picked.GroupId)) continue;
            await CreateGroupAssignmentAsync(appId, picked.GroupId, ParseIntent(picked.AssignmentType), picked.GroupName, log);
        }

        foreach (var existing in config.ExistingGroups)
        {
            if (string.IsNullOrWhiteSpace(existing.GroupName)) continue;
            var groupId = await ResolveGroupIdAsync(existing.GroupName, log);
            if (groupId == null)
            {
                log.Warning($"Group '{existing.GroupName}' not found in Entra ID - skipped");
                continue;
            }
            await CreateGroupAssignmentAsync(appId, groupId, existing.Intent, existing.GroupName, log);
        }

        if (config.CreateGroupPerPackage)
            await AssignPerPackageGroupAsync(appId, appInfo, config.GroupNameTemplate, config.NewGroupIntent, log);

        if (config.CreateUninstallGroupPerPackage)
            await AssignPerPackageGroupAsync(appId, appInfo, config.UninstallGroupNameTemplate, AssignmentIntent.Uninstall, log);
    }

    /// <summary>Resolves or creates the per-package group and assigns it.</summary>
    private async Task AssignPerPackageGroupAsync(string appId, ApplicationInfo appInfo, string template, AssignmentIntent intent, UploadLogger log)
    {
        var name = GroupAssignmentNamer.Build(template, appInfo.Manufacturer, appInfo.Name, appInfo.Version);
        if (string.IsNullOrWhiteSpace(name))
        {
            log.Warning($"Per-package group name for the {intent} assignment resolved to empty - skipped");
            return;
        }
        // Reuse an existing group with this name before creating one.
        var groupId = await ResolveGroupIdAsync(name, log) ?? await CreateSecurityGroupAsync(name, log);
        if (groupId != null)
            await CreateGroupAssignmentAsync(appId, groupId, intent, name, log);
    }

    private async Task<string?> ResolveGroupIdAsync(string displayName, UploadLogger log)
    {
        try
        {
            var filter = Uri.EscapeDataString($"displayName eq '{OData.Literal(displayName)}'");
            var url = $"{GraphBeta}/groups?$filter={filter}&$select=id";
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url);
            var response = await sharedHttpClient!.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.GetArrayLength() > 0)
                return arr[0].TryGetProperty("id", out var id) ? id.GetString() : null;
            return null;
        }
        catch (Exception ex)
        {
            log.Warning($"Could not look up group '{displayName}': {ex.Message}");
            return null;
        }
    }

    private async Task<string?> CreateSecurityGroupAsync(string displayName, UploadLogger log)
    {
        try
        {
            var payload = new
            {
                displayName,
                mailEnabled = false,
                mailNickname = SanitizeMailNickname(displayName),
                securityEnabled = true,
                groupTypes = Array.Empty<string>(),
            };
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, $"{GraphBeta}/groups");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await sharedHttpClient!.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                log.Warning($"Could not create group '{displayName}' (HTTP {(int)response.StatusCode}): {body}");
                return null;
            }
            using var doc = JsonDocument.Parse(body);
            var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            log.Success($"Created group '{displayName}'");
            return id;
        }
        catch (Exception ex)
        {
            log.Warning($"Could not create group '{displayName}': {ex.Message}");
            return null;
        }
    }

    private async Task CreateGroupAssignmentAsync(string appId, string groupId, AssignmentIntent intent, string groupName, UploadLogger log)
    {
        try
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
            using var request = await CreateAuthenticatedRequestAsync(
                HttpMethod.Post, $"{GraphBeta}/deviceAppManagement/mobileApps/{appId}/assignments");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await sharedHttpClient!.SendAsync(request);
            if (response.IsSuccessStatusCode)
                log.Success($"Assigned '{groupName}' ({intent})");
            else
                log.Warning($"Could not assign '{groupName}' (HTTP {(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }
        catch (Exception ex)
        {
            log.Warning($"Could not assign '{groupName}': {ex.Message}");
        }
    }

    private static AssignmentIntent ParseIntent(string intent) => intent?.ToLowerInvariant() switch
    {
        "uninstall" => AssignmentIntent.Uninstall,
        "available" => AssignmentIntent.Available,
        _ => AssignmentIntent.Required,
    };

    private static string SanitizeMailNickname(string displayName)
    {
        var chars = displayName.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray();
        var nickname = new string(chars);
        return string.IsNullOrEmpty(nickname) ? "group" : nickname;
    }
}
