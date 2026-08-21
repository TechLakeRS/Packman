using Packman.Helpers;
using Packman.Models;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Packman.Services;

/// <summary>Reads Win32 LOB applications from Intune via Graph.</summary>
public partial class IntuneService
{
    private const string Base = "https://graph.microsoft.com/beta/deviceAppManagement/mobileApps";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    private readonly Func<Task<string>> _tokenProvider;

    // Swapped by reference, not mutated: reads and delete-invalidation can race.
    private volatile IReadOnlyList<IntuneApplication>? _listCache;

    public IntuneService(Func<Task<string>> tokenProvider)
        => _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

    private async Task<HttpRequestMessage> AuthRequestAsync(HttpMethod method, string url)
    {
        var token = await _tokenProvider();
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    // ── List ────────────────────────────────────────────
    public async Task<List<IntuneApplication>> GetApplicationsAsync(bool forceRefresh = false, IProgress<int>? progress = null)
    {
        var cached = _listCache;
        if (!forceRefresh && cached != null)
            return cached.ToList();

        var apps = new List<IntuneApplication>();
        var url = $"{Base}?$filter=isof('microsoft.graph.win32LobApp')&$expand=categories&$top=100&$orderby=displayName";

        while (!string.IsNullOrEmpty(url))
        {
            using var request = await AuthRequestAsync(HttpMethod.Get, url);
            var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            // $expand=categories is occasionally rejected; retry without it.
            if (!response.IsSuccessStatusCode && url.Contains("&$expand=categories"))
            {
                url = url.Replace("&$expand=categories", "");
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to fetch applications ({(int)response.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("value", out var value))
                foreach (var el in value.EnumerateArray())
                    apps.Add(ParseListItem(el));

            progress?.Report(apps.Count);
            url = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() ?? "" : "";
        }

        var sorted = apps.OrderBy(a => a.DisplayName).ToList();
        _listCache = sorted;
        return sorted.ToList();
    }

    // ── Detail ──────────────────────────────────────────
    public async Task<ApplicationDetail> GetApplicationDetailAsync(string id)
    {
        using var request = await AuthRequestAsync(HttpMethod.Get, $"{Base}/{id}");
        var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to get app details ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var app = doc.RootElement;

        var detail = new ApplicationDetail
        {
            Id = app.GetSafeString("id"),
            DisplayName = app.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "Unknown" : "Unknown",
            Version = app.TryGetProperty("displayVersion", out var ver) ? ver.GetString() ?? "" : "",
            Publisher = app.TryGetProperty("publisher", out var pub) ? pub.GetString() ?? "" : "",
            Description = app.GetSafeString("description"),
            InstallCommand = app.TryGetProperty("installCommandLine", out var ic) ? ic.GetString() ?? "" : "",
            UninstallCommand = app.TryGetProperty("uninstallCommandLine", out var uc) ? uc.GetString() ?? "" : "",
            Owner = app.GetSafeString("owner"),
            Developer = app.GetSafeString("developer"),
            Notes = app.GetSafeString("notes"),
            FileName = app.GetSafeString("fileName"),
            SetupFilePath = app.GetSafeString("setupFilePath"),
            Size = app.GetSafeLong("size"),
            CreatedDateTime = app.GetSafeDateTime("createdDateTime"),
            LastModifiedDateTime = app.GetSafeDateTime("lastModifiedDateTime"),
            PublishingState = ReadStateString(app, "publishingState"),
        };
        detail.LastModified = detail.LastModifiedDateTime;

        if (app.TryGetProperty("installExperience", out var ie) && ie.ValueKind == JsonValueKind.Object)
        {
            if (ie.TryGetProperty("runAsAccount", out var acc))
            {
                var runAs = acc.ValueKind == JsonValueKind.Number ? acc.GetInt32().ToString() : acc.GetString();
                detail.InstallContext = string.Equals(runAs, "user", StringComparison.OrdinalIgnoreCase) ? "User" : "System";
            }
            if (ie.TryGetProperty("deviceRestartBehavior", out var rb) && rb.ValueKind == JsonValueKind.String)
                detail.RestartBehavior = rb.GetString() ?? "";
            if (ie.TryGetProperty("maxRunTimeInMinutes", out var mrt) && mrt.ValueKind == JsonValueKind.Number)
                detail.MaxRunTimeMinutes = mrt.GetInt32();
        }
        if (app.TryGetProperty("minimumFreeDiskSpaceInMB", out var mds) && mds.ValueKind == JsonValueKind.Number)
            detail.MinDiskSpaceMB = mds.GetInt32();

        detail.DetectionRules = ParseDetectionRules(app);
        detail.AssignedGroups = await GetAssignedGroupsAsync(id);
        detail.Statistics = await GetInstallationStatisticsAsync(id);
        return detail;
    }

    // ── Assignments ─────────────────────────────────────
    public async Task<List<AssignedGroup>> GetAssignedGroupsAsync(string appId)
    {
        var groups = new List<AssignedGroup>();
        try
        {
            using var request = await AuthRequestAsync(HttpMethod.Get, $"{Base}/{appId}/assignments");
            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return groups;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return groups;

            var needNames = new List<(AssignedGroup group, string groupId)>();
            foreach (var a in arr.EnumerateArray())
            {
                var assignmentId = a.GetSafeString("id");
                var intent = a.TryGetProperty("intent", out var ip) ? ip.GetString() ?? "Unknown" : "Unknown";
                if (!a.TryGetProperty("target", out var t)) continue;
                var type = t.TryGetProperty("@odata.type", out var tp) ? tp.GetString() ?? "" : "";

                switch (type)
                {
                    case "#microsoft.graph.groupAssignmentTarget":
                        var gid = t.TryGetProperty("groupId", out var g) ? g.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(gid)) break;
                        var group = new AssignedGroup { AssignmentId = assignmentId, GroupId = gid, GroupName = $"Group {Shorten(gid)}", AssignmentType = intent };
                        groups.Add(group);
                        needNames.Add((group, gid));
                        break;
                    case "#microsoft.graph.allLicensedUsersAssignmentTarget":
                        groups.Add(new AssignedGroup { AssignmentId = assignmentId, GroupName = "All Licensed Users", AssignmentType = intent });
                        break;
                    case "#microsoft.graph.allDevicesAssignmentTarget":
                        groups.Add(new AssignedGroup { AssignmentId = assignmentId, GroupName = "All Devices", AssignmentType = intent });
                        break;
                    default:
                        groups.Add(new AssignedGroup { AssignmentId = assignmentId, GroupName = type.Replace("#microsoft.graph.", ""), AssignmentType = intent });
                        break;
                }
            }

            // Needs directory read permission; ignored when it fails.
            await Task.WhenAll(needNames.Select(async item =>
            {
                var name = await GetGroupNameAsync(item.groupId);
                if (!string.IsNullOrEmpty(name)) item.group.GroupName = name;
            }));
        }
        catch
        {
            // The detail screen tolerates an empty list.
        }
        return groups;
    }

    private async Task<string?> GetGroupNameAsync(string groupId)
    {
        try
        {
            using var request = await AuthRequestAsync(HttpMethod.Get, $"https://graph.microsoft.com/beta/groups/{groupId}?$select=displayName");
            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("displayName", out var n) ? n.GetString() : null;
        }
        catch { return null; }
    }

    // ── Install statistics (reporting endpoint) ─────────
    public async Task<InstallationStatistics> GetInstallationStatisticsAsync(string appId)
    {
        try
        {
            var url = "https://graph.microsoft.com/beta/deviceManagement/reports/getAppStatusOverviewReport";
            using var request = await AuthRequestAsync(HttpMethod.Post, url);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { filter = $"(ApplicationId eq '{OData.Literal(appId)}')" }),
                Encoding.UTF8, "application/json");
            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new InstallationStatistics();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("Values", out var values) || values.ValueKind != JsonValueKind.Array)
                return new InstallationStatistics();

            foreach (var row in values.EnumerateArray())
            {
                var cells = row.EnumerateArray().ToList();
                if (cells.Count < 6) continue;
                // [0]AppId [1]Failed [2]Pending [3]Installed [4]NotInstalled [5]NotApplicable
                var stats = new InstallationStatistics
                {
                    FailedInstalls = cells[1].GetSafeInt(),
                    PendingInstalls = cells[2].GetSafeInt(),
                    SuccessfulInstalls = cells[3].GetSafeInt(),
                    NotInstalled = cells[4].GetSafeInt(),
                    NotApplicable = cells[5].GetSafeInt(),
                };
                stats.TotalDevices = stats.SuccessfulInstalls + stats.FailedInstalls
                    + stats.PendingInstalls + stats.NotInstalled + stats.NotApplicable;
                return stats;
            }
            return new InstallationStatistics();
        }
        catch { return new InstallationStatistics(); }
    }

    // ── Delete ──────────────────────────────────────────
    public async Task DeleteApplicationAsync(string id)
    {
        using var request = await AuthRequestAsync(HttpMethod.Delete, $"{Base}/{id}");
        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to delete app ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");

        var cached = _listCache;
        if (cached != null)
            _listCache = cached.Where(a => a.Id != id).ToList();
    }

    // ── Parsing ─────────────────────────────────────────
    private static IntuneApplication ParseListItem(JsonElement app)
    {
        var category = "Uncategorized";
        if (app.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            var names = new List<string>();
            foreach (var c in cats.EnumerateArray())
                if (c.TryGetProperty("displayName", out var cn) && !string.IsNullOrWhiteSpace(cn.GetString()))
                    names.Add(cn.GetString()!);
            if (names.Count > 0) category = string.Join(", ", names);
        }

        return new IntuneApplication
        {
            Id = app.GetSafeString("id"),
            DisplayName = app.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "Unknown" : "Unknown",
            Version = app.TryGetProperty("displayVersion", out var ver) ? ver.GetString() ?? "" : "",
            Publisher = app.TryGetProperty("publisher", out var pub) ? pub.GetString() ?? "" : "",
            Category = category,
            LastModified = app.GetSafeDateTime("lastModifiedDateTime"),
            PublishingState = ReadStateString(app, "publishingState"),
        };
    }

    private static List<DetectionRule> ParseDetectionRules(JsonElement app)
    {
        var rules = new List<DetectionRule>();
        if (!app.TryGetProperty("detectionRules", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return rules;

        foreach (var r in arr.EnumerateArray())
        {
            var type = r.TryGetProperty("@odata.type", out var tp) ? tp.GetString() ?? "" : "";
            switch (type)
            {
                case "#microsoft.graph.win32LobAppProductCodeDetection":
                    var pv = r.GetSafeString("productVersion");
                    rules.Add(new DetectionRule
                    {
                        Type = DetectionRuleType.MSI,
                        Path = r.GetSafeString("productCode"),
                        CheckVersion = !string.IsNullOrEmpty(pv),
                        Operator = r.GetSafeString("productVersionOperator"),
                        FileOrFolderName = pv,
                    });
                    break;
                case "#microsoft.graph.win32LobAppFileSystemDetection":
                    rules.Add(new DetectionRule
                    {
                        Type = DetectionRuleType.File,
                        Path = r.GetSafeString("path"),
                        FileOrFolderName = r.GetSafeString("fileOrFolderName"),
                        DetectionType = r.GetSafeString("detectionType"),
                        Operator = r.GetSafeString("operator"),
                        DetectionValue = r.GetSafeString("detectionValue"),
                        Check32BitOn64System = GetSafeBool(r, "check32BitOn64System"),
                    });
                    break;
                case "#microsoft.graph.win32LobAppRegistryDetection":
                    rules.Add(new DetectionRule
                    {
                        Type = DetectionRuleType.Registry,
                        Path = r.GetSafeString("keyPath"),
                        FileOrFolderName = r.GetSafeString("valueName"),
                        DetectionType = r.GetSafeString("detectionType"),
                        Operator = r.GetSafeString("operator"),
                        DetectionValue = r.GetSafeString("detectionValue"),
                        Check32BitOn64System = GetSafeBool(r, "check32BitOn64System"),
                    });
                    break;
                case "#microsoft.graph.win32LobAppPowerShellScriptDetection":
                    rules.Add(new DetectionRule
                    {
                        Type = DetectionRuleType.Script,
                        ScriptContent = r.GetSafeString("scriptContent"),
                        EnforceSignatureCheck = GetSafeBool(r, "enforceSignatureCheck"),
                        RunAs32Bit = GetSafeBool(r, "runAs32Bit"),
                    });
                    break;
            }
        }
        return rules;
    }

    private static bool GetSafeBool(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static string ReadStateString(JsonElement app, string prop)
    {
        if (!app.TryGetProperty(prop, out var v)) return "";
        return v.ValueKind == JsonValueKind.Number ? v.GetInt32().ToString() : v.GetString() ?? "";
    }

    private static string Shorten(string id) => id.Length > 8 ? id[..8] + "…" : id;
}
