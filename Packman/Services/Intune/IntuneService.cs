using Packman.Helpers;
using Packman.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Packman.Services;

/// <summary>Reads and edits Win32 LOB applications in Intune via Graph.</summary>
public partial class IntuneService
{
    private const string Base = GraphClient.MobileApps;

    private readonly GraphClient _graph;

    // Swapped by reference, not mutated: reads and delete-invalidation can race.
    private volatile IReadOnlyList<IntuneApplication>? _listCache;

    public IntuneService(Func<Task<string>> tokenProvider)
        => _graph = new GraphClient(tokenProvider);

    // ── List ────────────────────────────────────────────
    public async Task<List<IntuneApplication>> GetApplicationsAsync(bool forceRefresh = false, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var cached = _listCache;
        if (!forceRefresh && cached != null)
            return cached.ToList();

        var apps = new List<IntuneApplication>();
        var url = $"{Base}?$filter=isof('microsoft.graph.win32LobApp')&$expand=categories&$top=100&$orderby=displayName";

        while (!string.IsNullOrEmpty(url))
        {
            var response = await _graph.GetAsync(url, "Fetch applications", ct, throwOnError: false);
            if (!response.IsSuccess)
            {
                // Graph rejects $expand=categories now and then with a 400; drop it and go on
                // without categories. Anything else is a real failure.
                if (response.StatusCode == 400 && url.Contains("&$expand=categories"))
                {
                    url = url.Replace("&$expand=categories", "");
                    continue;
                }
                throw response.ToException("Fetch applications");
            }

            var page = response.Json;
            if (page.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
                foreach (var el in value.EnumerateArray())
                    apps.Add(ParseListItem(el));

            progress?.Report(apps.Count);
            url = page.GetSafeString("@odata.nextLink");
        }

        var sorted = apps.OrderBy(a => a.DisplayName).ToList();
        _listCache = sorted;
        return sorted.ToList();
    }

    // ── Detail ──────────────────────────────────────────
    public async Task<ApplicationDetail> GetApplicationDetailAsync(string id, CancellationToken ct = default)
    {
        var app = (await _graph.GetAsync($"{Base}/{id}", "Get app details", ct)).Json;

        var detail = new ApplicationDetail
        {
            Id = app.GetSafeString("id"),
            DisplayName = app.GetSafeString("displayName") is { Length: > 0 } dn ? dn : "Unknown",
            Version = app.GetSafeString("displayVersion"),
            Publisher = app.GetSafeString("publisher"),
            Description = app.GetSafeString("description"),
            InstallCommand = app.GetSafeString("installCommandLine"),
            UninstallCommand = app.GetSafeString("uninstallCommandLine"),
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
            var runAs = ReadStateString(ie, "runAsAccount");
            detail.InstallContext = string.Equals(runAs, "user", StringComparison.OrdinalIgnoreCase) ? "User" : "System";
            detail.RestartBehavior = ie.GetSafeString("deviceRestartBehavior");
            detail.MaxRunTimeMinutes = ie.GetSafeInt("maxRunTimeInMinutes");
        }
        detail.MinDiskSpaceMB = app.GetSafeInt("minimumFreeDiskSpaceInMB");
        detail.DetectionRules = DetectionRuleGraph.Parse(app);

        // Independent reads; together they are the slow half of opening a detail page.
        var groups = GetAssignedGroupsAsync(id, ct);
        var stats = GetInstallationStatisticsAsync(id, ct);
        await Task.WhenAll(groups, stats);
        detail.AssignedGroups = groups.Result;
        detail.Statistics = stats.Result;
        return detail;
    }

    // ── Assignments ─────────────────────────────────────
    /// <summary>The app's assignments. Empty when they cannot be read; the detail page tolerates that.</summary>
    public async Task<List<AssignedGroup>> GetAssignedGroupsAsync(string appId, CancellationToken ct = default)
    {
        var groups = new List<AssignedGroup>();
        var response = await _graph.GetAsync($"{Base}/{appId}/assignments", "Read assignments", ct, throwOnError: false);
        if (!response.IsSuccess)
        {
            Debug.WriteLine($"Assignments unavailable for {appId}: HTTP {response.StatusCode}");
            return groups;
        }

        var root = response.Json;
        if (!root.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return groups;

        var needNames = new List<(AssignedGroup group, string groupId)>();
        foreach (var a in arr.EnumerateArray())
        {
            var assignmentId = a.GetSafeString("id");
            var intent = a.GetSafeString("intent") is { Length: > 0 } i ? i : "Unknown";
            if (!a.TryGetProperty("target", out var t)) continue;
            var type = t.GetSafeString("@odata.type");

            switch (type)
            {
                case "#microsoft.graph.groupAssignmentTarget":
                    var gid = t.GetSafeString("groupId");
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

        // Needs directory read permission; the id stays on display when it fails.
        await Task.WhenAll(needNames.Select(async item =>
        {
            var name = await GetGroupNameAsync(item.groupId, ct);
            if (!string.IsNullOrEmpty(name)) item.group.GroupName = name;
        }));
        return groups;
    }

    private async Task<string?> GetGroupNameAsync(string groupId, CancellationToken ct)
    {
        var response = await _graph.GetAsync($"{GraphClient.Groups}/{groupId}?$select=displayName", "Read group name", ct, throwOnError: false);
        return response.IsSuccess ? response.Json.GetSafeString("displayName") : null;
    }

    // ── Install statistics (reporting endpoint) ─────────
    /// <summary>Device install rollup. Zeros when the report is unavailable.</summary>
    public async Task<InstallationStatistics> GetInstallationStatisticsAsync(string appId, CancellationToken ct = default)
    {
        var response = await _graph.PostAsync(
            $"{GraphClient.Beta}/deviceManagement/reports/getAppStatusOverviewReport",
            new { filter = $"(ApplicationId eq '{OData.Literal(appId)}')" },
            "App status report", ct, throwOnError: false);
        if (!response.IsSuccess)
        {
            Debug.WriteLine($"App status report unavailable for {appId}: HTTP {response.StatusCode}");
            return new InstallationStatistics();
        }

        var root = response.Json;
        if (!root.TryGetProperty("Values", out var values) || values.ValueKind != JsonValueKind.Array)
            return new InstallationStatistics();

        // Columns are read by name from the Schema the report ships, so a reordered or
        // added column cannot silently shift the numbers.
        var columns = ReadReportColumns(root);
        int Cell(List<JsonElement> row, string column, int fallbackIndex)
        {
            var index = columns.TryGetValue(column, out var i) ? i : fallbackIndex;
            return index >= 0 && index < row.Count ? row[index].GetSafeInt() : 0;
        }

        foreach (var rowElement in values.EnumerateArray())
        {
            if (rowElement.ValueKind != JsonValueKind.Array) continue;
            var row = rowElement.EnumerateArray().ToList();
            var stats = new InstallationStatistics
            {
                FailedInstalls = Cell(row, "FailedDeviceCount", 1),
                PendingInstalls = Cell(row, "PendingInstallDeviceCount", 2),
                SuccessfulInstalls = Cell(row, "InstalledDeviceCount", 3),
                NotInstalled = Cell(row, "NotInstalledDeviceCount", 4),
                NotApplicable = Cell(row, "NotApplicableDeviceCount", 5),
            };
            stats.TotalDevices = stats.SuccessfulInstalls + stats.FailedInstalls
                + stats.PendingInstalls + stats.NotInstalled + stats.NotApplicable;
            return stats;
        }
        return new InstallationStatistics();
    }

    private static Dictionary<string, int> ReadReportColumns(JsonElement root)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("Schema", out var schema) || schema.ValueKind != JsonValueKind.Array)
            return columns;

        var index = 0;
        foreach (var column in schema.EnumerateArray())
        {
            var name = column.GetSafeString("Column");
            if (!string.IsNullOrEmpty(name)) columns[name] = index;
            index++;
        }
        return columns;
    }

    // ── Delete ──────────────────────────────────────────
    public async Task DeleteApplicationAsync(string id, CancellationToken ct = default)
    {
        await _graph.DeleteAsync($"{Base}/{id}", "Delete app", ct);

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
            var names = cats.EnumerateArray()
                .Select(c => c.GetSafeString("displayName"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            if (names.Count > 0) category = string.Join(", ", names);
        }

        return new IntuneApplication
        {
            Id = app.GetSafeString("id"),
            DisplayName = app.GetSafeString("displayName") is { Length: > 0 } dn ? dn : "Unknown",
            Version = app.GetSafeString("displayVersion"),
            Publisher = app.GetSafeString("publisher"),
            Category = category,
            LastModified = app.GetSafeDateTime("lastModifiedDateTime"),
            PublishingState = ReadStateString(app, "publishingState"),
        };
    }

    // Enum-ish properties come back as strings on beta and as numbers on some older records.
    private static string ReadStateString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return "";
        return v.ValueKind == JsonValueKind.Number ? v.GetInt32().ToString() : v.GetString() ?? "";
    }

    private static string Shorten(string id) => id.Length > 8 ? id[..8] + "…" : id;
}
