using Packman.Models;
using Packman.Services;
using System.Collections.ObjectModel;

namespace Packman.ViewModels;

/// <summary>
/// The Advanced screen's three directory tools: bulk-add PCs to a group, a PC's groups,
/// and the apps targeting a group.
/// </summary>
public sealed class AdvancedViewModel : ObservableObject
{
    private static readonly char[] NameSeparators = { '\r', '\n', ',', ';', '\t', ' ' };

    private readonly IntuneService _apps = AppServices.Apps;
    private readonly IntuneAuthService _auth = AppServices.Auth;

    public AsyncRelayCommand BulkAddCommand { get; }
    public RelayCommand ConnectCommand { get; }

    /// <summary>Raised on "connect"; the host switches to Settings.</summary>
    public event Action? ConnectRequested;

    public AdvancedViewModel()
    {
        BulkAddCommand = new AsyncRelayCommand(RunBulkAddAsync, () => CanBulkAdd);
        ConnectCommand = new RelayCommand(() => ConnectRequested?.Invoke());
    }

    public bool IsSignedIn => _auth.IsSignedIn;
    public bool ShowConnectPrompt => !_auth.IsSignedIn;

    /// <summary>Re-reads sign-in state. Called each time the screen is shown.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(ShowConnectPrompt));
    }

    // ══ 1. Bulk add PCs to a group ══════════════════════════════

    private string _bulkGroupSearch = "";
    public string BulkGroupSearch
    {
        get => _bulkGroupSearch;
        set
        {
            if (!Set(ref _bulkGroupSearch, value)) return;
            _bulkGroup = null;
            OnPropertyChanged(nameof(CanBulkAdd));
            RaiseBulkGroupCheck();
            BulkAddCommand.RaiseCanExecuteChanged();
            _ = SearchGroupsAsync(BulkGroupSlot, value, BulkGroupResults, OnBulkGroupResults);
        }
    }

    public ObservableCollection<EntraGroup> BulkGroupResults { get; } = new();
    public bool HasBulkGroupResults => BulkGroupResults.Count > 0;

    private EntraGroup? _bulkGroup;

    /// <summary>An exact typed name counts as picking the group.</summary>
    private void OnBulkGroupResults()
    {
        OnPropertyChanged(nameof(HasBulkGroupResults));
        var exact = BulkGroupResults.FirstOrDefault(
            g => g.DisplayName.Equals(_bulkGroupSearch.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact != null) SelectBulkGroup(exact);
    }

    public void SelectBulkGroup(EntraGroup group)
    {
        _bulkGroup = group;
        _bulkGroupSearch = group.DisplayName;   // field write: don't retrigger the search
        OnPropertyChanged(nameof(BulkGroupSearch));
        BulkGroupResults.Clear();
        OnPropertyChanged(nameof(HasBulkGroupResults));
        OnPropertyChanged(nameof(CanBulkAdd));
        RaiseBulkGroupCheck();
        BulkAddCommand.RaiseCanExecuteChanged();
    }

    /// <summary>True once a group was picked, so the name is known to exist.</summary>
    public bool IsBulkGroupConfirmed => _bulkGroup != null;

    public string BulkGroupCheck => _bulkGroup != null
        ? $"Group found — {_bulkGroup.DisplayName}"
        : string.IsNullOrWhiteSpace(_bulkGroupSearch) ? "" : "Pick the group from the results to confirm it exists.";

    public bool HasBulkGroupCheck => !string.IsNullOrEmpty(BulkGroupCheck);

    private void RaiseBulkGroupCheck()
    {
        OnPropertyChanged(nameof(IsBulkGroupConfirmed));
        OnPropertyChanged(nameof(BulkGroupCheck));
        OnPropertyChanged(nameof(HasBulkGroupCheck));
    }

    private string _pcNames = "";
    public string PcNames
    {
        get => _pcNames;
        set
        {
            if (!Set(ref _pcNames, value)) return;
            OnPropertyChanged(nameof(PcNameCountText));
            OnPropertyChanged(nameof(CanBulkAdd));
            BulkAddCommand.RaiseCanExecuteChanged();
        }
    }

    public string PcNameCountText
    {
        get
        {
            var n = ParseNames().Count;
            return n == 0 ? "" : $"{n} PC name{(n == 1 ? "" : "s")}";
        }
    }

    public ObservableCollection<BulkAddRow> BulkRows { get; } = new();
    public bool HasBulkRows => BulkRows.Count > 0;

    private bool _isBulkRunning;
    public bool IsBulkRunning
    {
        get => _isBulkRunning;
        private set
        {
            if (!Set(ref _isBulkRunning, value)) return;
            OnPropertyChanged(nameof(CanBulkAdd));
            BulkAddCommand.RaiseCanExecuteChanged();
        }
    }

    private string _bulkStatus = "";
    public string BulkStatus
    {
        get => _bulkStatus;
        private set { if (Set(ref _bulkStatus, value)) OnPropertyChanged(nameof(HasBulkStatus)); }
    }
    public bool HasBulkStatus => !string.IsNullOrEmpty(_bulkStatus);

    public bool CanBulkAdd => !IsBulkRunning && _bulkGroup != null && ParseNames().Count > 0;

    private List<string> ParseNames() =>
        _pcNames.Split(NameSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private async Task RunBulkAddAsync()
    {
        var group = _bulkGroup;
        var names = ParseNames();
        if (group == null || names.Count == 0) return;

        IsBulkRunning = true;
        BulkRows.Clear();
        OnPropertyChanged(nameof(HasBulkRows));
        BulkStatus = $"Looking up {names.Count} PC name{(names.Count == 1 ? "" : "s")}…";

        try
        {
            var found = await _apps.FindDevicesByNamesAsync(names);
            var added = 0; var already = 0; var missing = 0; var failed = 0;

            foreach (var name in names)
            {
                var devices = found.TryGetValue(name, out var d) ? d : new List<EntraDevice>();
                if (devices.Count == 0)
                {
                    BulkRows.Add(new BulkAddRow { PcName = name, Status = BulkAddRow.StatusNotFound, Detail = "No Entra device record with this name" });
                    missing++;
                    continue;
                }

                var row = new BulkAddRow { PcName = name };
                var addedHere = 0; string? error = null;

                // Re-enrolled machines leave several records; add all so the live one is covered.
                foreach (var device in devices)
                {
                    try
                    {
                        if (await _apps.TryAddGroupMemberAsync(group.Id, device.Id)) addedHere++;
                    }
                    catch (Exception ex) { error = ex.Message; }
                }

                var duplicateNote = devices.Count > 1 ? $" ({devices.Count} device records matched)" : "";
                if (error != null)
                {
                    row.Status = BulkAddRow.StatusFailed;
                    row.Detail = error;
                    failed++;
                }
                else if (addedHere > 0)
                {
                    row.Status = BulkAddRow.StatusAdded;
                    row.Detail = $"Added to {group.DisplayName}{duplicateNote}";
                    added++;
                }
                else
                {
                    row.Status = BulkAddRow.StatusAlready;
                    row.Detail = $"Already in {group.DisplayName}{duplicateNote}";
                    already++;
                }
                BulkRows.Add(row);

                BulkStatus = $"Processing… {BulkRows.Count} of {names.Count}";
                OnPropertyChanged(nameof(HasBulkRows));
            }

            BulkStatus = $"{added} added · {already} already members · {missing} not found · {failed} failed";
        }
        catch (Exception ex)
        {
            BulkStatus = $"Bulk add stopped: {ex.Message}";
        }
        finally
        {
            IsBulkRunning = false;
            OnPropertyChanged(nameof(HasBulkRows));
        }
    }

    // ══ 2. PC → groups ══════════════════════════════════════════

    private string _deviceSearch = "";
    public string DeviceSearch
    {
        get => _deviceSearch;
        set
        {
            if (!Set(ref _deviceSearch, value)) return;
            _selectedDevice = null;
            _ = RunDeviceSearchAsync(value);
        }
    }

    public ObservableCollection<EntraDevice> DeviceResults { get; } = new();
    public bool HasDeviceResults => DeviceResults.Count > 0;

    private EntraDevice? _selectedDevice;

    private int _deviceSearchSeq;
    private async Task RunDeviceSearchAsync(string query)
    {
        var seq = ++_deviceSearchSeq;
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            DeviceResults.Clear();
            OnPropertyChanged(nameof(HasDeviceResults));
            return;
        }
        try
        {
            var results = await _apps.SearchDevicesAsync(query);
            if (seq != _deviceSearchSeq) return;   // stale response
            DeviceResults.Clear();
            foreach (var d in results) DeviceResults.Add(d);
        }
        catch
        {
            if (seq != _deviceSearchSeq) return;
            DeviceResults.Clear();
        }
        OnPropertyChanged(nameof(HasDeviceResults));
    }

    public void SelectDeviceResult(EntraDevice device)
    {
        _selectedDevice = device;
        _deviceSearch = device.DisplayName;
        OnPropertyChanged(nameof(DeviceSearch));
        DeviceResults.Clear();
        OnPropertyChanged(nameof(HasDeviceResults));
        _ = LoadDeviceGroupsAsync();
    }

    public ObservableCollection<DeviceGroupMembership> DeviceGroups { get; } = new();
    public bool HasDeviceGroups => DeviceGroups.Count > 0;

    private bool _isDeviceLoading;
    public bool IsDeviceLoading { get => _isDeviceLoading; private set => Set(ref _isDeviceLoading, value); }

    private string _deviceStatus = "";
    public string DeviceStatus
    {
        get => _deviceStatus;
        private set { if (Set(ref _deviceStatus, value)) OnPropertyChanged(nameof(HasDeviceStatus)); }
    }
    public bool HasDeviceStatus => !string.IsNullOrEmpty(_deviceStatus);

    private async Task LoadDeviceGroupsAsync()
    {
        // No "already loading" guard: the ReferenceEquals check below drops the stale result,
        // and a guard here would drop the newer selection instead.
        var device = _selectedDevice;
        if (device == null) return;

        IsDeviceLoading = true;
        DeviceGroups.Clear();
        OnPropertyChanged(nameof(HasDeviceGroups));
        DeviceStatus = $"Reading group membership for {device.DisplayName}…";
        try
        {
            var groups = await _apps.GetDeviceGroupsAsync(device.Id);
            if (!ReferenceEquals(_selectedDevice, device)) return;   // selection changed meanwhile
            foreach (var g in groups) DeviceGroups.Add(g);
            DeviceStatus = groups.Count == 0
                ? $"{device.DisplayName} is not a member of any group."
                : $"{device.DisplayName} · {groups.Count} group{(groups.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            DeviceStatus = $"Could not read group membership: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_selectedDevice, device)) IsDeviceLoading = false;
            OnPropertyChanged(nameof(HasDeviceGroups));
        }
    }

    // ══ 3. Group → apps ═════════════════════════════════════════

    private string _appGroupSearch = "";
    public string AppGroupSearch
    {
        get => _appGroupSearch;
        set
        {
            if (!Set(ref _appGroupSearch, value)) return;
            _selectedAppGroup = null;
            _ = SearchGroupsAsync(AppGroupSlot, value, AppGroupResults, () => OnPropertyChanged(nameof(HasAppGroupResults)));
        }
    }

    public ObservableCollection<EntraGroup> AppGroupResults { get; } = new();
    public bool HasAppGroupResults => AppGroupResults.Count > 0;

    private EntraGroup? _selectedAppGroup;

    public void SelectAppGroup(EntraGroup group)
    {
        _selectedAppGroup = group;
        _appGroupSearch = group.DisplayName;
        OnPropertyChanged(nameof(AppGroupSearch));
        AppGroupResults.Clear();
        OnPropertyChanged(nameof(HasAppGroupResults));
        _ = LoadGroupAppsAsync();
    }

    public ObservableCollection<GroupAppAssignment> GroupApps { get; } = new();
    public bool HasGroupApps => GroupApps.Count > 0;

    private bool _isAppScanLoading;
    public bool IsAppScanLoading { get => _isAppScanLoading; private set => Set(ref _isAppScanLoading, value); }

    private string _appScanStatus = "";
    public string AppScanStatus
    {
        get => _appScanStatus;
        private set { if (Set(ref _appScanStatus, value)) OnPropertyChanged(nameof(HasAppScanStatus)); }
    }
    public bool HasAppScanStatus => !string.IsNullOrEmpty(_appScanStatus);

    private async Task LoadGroupAppsAsync()
    {
        var group = _selectedAppGroup;
        if (group == null) return;

        IsAppScanLoading = true;
        GroupApps.Clear();
        OnPropertyChanged(nameof(HasGroupApps));
        AppScanStatus = "Scanning app assignments…";
        try
        {
            var progress = new Progress<int>(n => AppScanStatus = $"Scanning app assignments… {n} apps checked");
            var apps = await _apps.GetGroupAppAssignmentsAsync(group.Id, progress);
            if (!ReferenceEquals(_selectedAppGroup, group)) return;   // selection changed meanwhile
            foreach (var a in apps) GroupApps.Add(a);
            AppScanStatus = apps.Count == 0
                ? $"No apps are assigned to {group.DisplayName}."
                : $"{group.DisplayName} · {apps.Count} app{(apps.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            AppScanStatus = $"Could not scan assignments: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_selectedAppGroup, group)) IsAppScanLoading = false;
            OnPropertyChanged(nameof(HasGroupApps));
        }
    }

    // ── Shared group search (one sequence guard per search box) ──
    private const int BulkGroupSlot = 0;
    private const int AppGroupSlot = 1;
    private readonly int[] _groupSearchSeq = new int[2];

    private async Task SearchGroupsAsync(int slot, string query, ObservableCollection<EntraGroup> target, Action notify)
    {
        var seq = ++_groupSearchSeq[slot];
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            target.Clear();
            notify();
            return;
        }
        try
        {
            var results = await _apps.SearchGroupsAsync(query);
            if (seq != _groupSearchSeq[slot]) return;   // stale response
            target.Clear();
            foreach (var g in results) target.Add(g);
        }
        catch
        {
            if (seq != _groupSearchSeq[slot]) return;
            target.Clear();
        }
        notify();
    }
}
