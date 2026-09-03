using Packman.Helpers;
using Packman.Models;
using Packman.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Packman.ViewModels;

/// <summary>
/// The standalone "Upload to Intune" page: pick a built package, review its detection
/// rules, choose assignment groups and publish.
/// </summary>
public sealed class UploadToIntuneViewModel : ObservableObject
{
    private readonly SettingsService _settings = AppServices.Settings;
    private readonly IntuneAuthService _auth = AppServices.Auth;

    /// <summary>Cancels the running upload. Null when idle.</summary>
    private CancellationTokenSource? _cts;

    public UploadToIntuneViewModel()
    {
        AddRuleCommand = new RelayCommand(AddDetectionRule, () => CanAddRule);
        RemoveRuleCommand = new RelayCommand<DetectionRule>(r => { if (r != null) DetectionRules.Remove(r); });
        UploadCommand = new AsyncRelayCommand(UploadAsync, () => UploadEnabled);
        CancelUploadCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        DoneCommand = new RelayCommand(ResetAfterPublish);

        PublishSteps = new ObservableCollection<PublishStepViewModel>
        {
            new(1, "Validating .intunewin package"),
            new(2, "Uploading to tenant"),
            new(3, "Creating Win32 app"),
            new(4, "Assigning to groups"),
        };
    }

    // ── Sign-in state ───────────────────────────────────
    public bool IsSignedIn => _auth.IsSignedIn;
    public bool IsNotSignedIn => !_auth.IsSignedIn;
    public string SignedInUser => _auth.SignedInUser ?? "";

    /// <summary>Tenant label taken from the signed-in UPN domain.</summary>
    public string TenantName
    {
        get
        {
            var upn = _auth.SignedInUser ?? "";
            var at = upn.IndexOf('@');
            if (at < 0 || at == upn.Length - 1) return "your";
            var domain = upn[(at + 1)..];
            var dot = domain.IndexOf('.');
            return dot > 0 ? domain[..dot] : domain;
        }
    }

    /// <summary>Refreshes sign-in dependent text.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsNotSignedIn));
        OnPropertyChanged(nameof(SignedInUser));
        OnPropertyChanged(nameof(TenantName));
        UploadCommand.RaiseCanExecuteChanged();
    }

    // ── Package selection ───────────────────────────────
    private string _packageRoot = "";
    public string PackageRoot { get => _packageRoot; private set => Set(ref _packageRoot, value); }

    private string _packageFolderName = "";
    public string PackageFolderName { get => _packageFolderName; private set => Set(ref _packageFolderName, value); }

    private bool _isValidated;
    public bool IsValidated
    {
        get => _isValidated;
        private set { if (Set(ref _isValidated, value)) UploadCommand.RaiseCanExecuteChanged(); }
    }

    private string _validationError = "";
    public string ValidationError { get => _validationError; private set => Set(ref _validationError, value); }
    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    private string _appName = "";
    public string AppName { get => _appName; private set => Set(ref _appName, value); }

    private string _manufacturer = "";
    public string Manufacturer { get => _manufacturer; private set => Set(ref _manufacturer, value); }

    private string _version = "";
    public string Version { get => _version; private set => Set(ref _version, value); }

    private string _installContext = "System";
    public string InstallContext { get => _installContext; private set => Set(ref _installContext, value); }

    private string _sizeText = "";
    public string SizeText { get => _sizeText; private set => Set(ref _sizeText, value); }

    public string DisplayTitle => $"{Manufacturer} {AppName}".Trim();

    private string _intuneDisplayName = "";
    /// <summary>Title in Intune. Seeded from the package metadata, editable before upload.</summary>
    public string IntuneDisplayName { get => _intuneDisplayName; set => Set(ref _intuneDisplayName, value); }

    /// <summary>
    /// Validates a folder as a v4 package and pulls metadata, install context and an
    /// MSI detection rule out of it.
    /// </summary>
    public void ProcessSelectedFolder(string selectedPath)
    {
        ValidationError = "";
        try
        {
            var root = FolderBrowserHelper.GetPackageRootPath(selectedPath);
            if (!FolderBrowserHelper.ValidatePackageStructure(root))
            {
                Fail("Invoke-AppDeployToolkit.exe not found. Select the package folder that contains the Application folder.");
                return;
            }

            var applicationFolder = Path.Combine(root, "Application");
            if (!Directory.Exists(applicationFolder))
                applicationFolder = root;

            var scriptPath = FolderBrowserHelper.GetPSADTScriptPath(applicationFolder);
            if (string.IsNullOrEmpty(scriptPath))
            {
                Fail("Invoke-AppDeployToolkit.ps1 not found in the Application folder.");
                return;
            }

            var meta = MetadataExtractor.ExtractMetadataFromScript(scriptPath);
            Manufacturer = meta.GetValueOrDefault("Vendor", "");
            AppName = meta.GetValueOrDefault("AppName", "");
            Version = meta.GetValueOrDefault("Version", "");
            InstallContext = InstallContextParser.ExtractFromPackage(root);

            IntuneDisplayName = GroupAssignmentNamer.Build(
                _settings.Settings.IntuneDefaults.DisplayNameTemplate, Manufacturer, AppName, Version);

            PackageRoot = root;
            PackageFolderName = new DirectoryInfo(root).Name;
            SizeText = ByteSize.Format(DirectoryCopy.TotalSize(root));

            BuildDetectionRules(root);
            IsValidated = true;

            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(HasValidationError));
        }
        catch (Exception ex)
        {
            Fail($"Could not read package: {ex.Message}");
        }
    }

    private void Fail(string message)
    {
        IsValidated = false;
        ValidationError = message;
        OnPropertyChanged(nameof(HasValidationError));
    }

    // ── Detection rules ─────────────────────────────────
    public ObservableCollection<DetectionRule> DetectionRules { get; } = new();

    private void BuildDetectionRules(string root)
    {
        DetectionRules.Clear();
        _msiProductCode = "";
        try
        {
            var filesFolder = Path.Combine(root, "Application", "Files");
            if (Directory.Exists(filesFolder))
            {
                var msiFiles = Directory.GetFiles(filesFolder, "*.msi", SearchOption.TopDirectoryOnly);
                if (msiFiles.Length > 0)
                {
                    var msi = MsiInfoService.ExtractMsiInfo(msiFiles[0]);
                    if (msi.IsValid)
                    {
                        _msiProductCode = msi.ProductCode;
                        DetectionRules.Add(new DetectionRule
                        {
                            Type = DetectionRuleType.File,
                            Path = "%ProgramFiles%",
                            FileOrFolderName = $"{AppName}.exe",
                            DetectionType = "version",
                            Operator = "greaterThanOrEqual",
                            DetectionValue = string.IsNullOrEmpty(msi.ProductVersion) ? Version : msi.ProductVersion,
                            CheckVersion = true,
                            Check32BitOn64System = true,
                        });
                    }
                }
            }
        }
        catch { /* leave list empty; user can add a rule manually */ }

        NewRuleProductCode = _msiProductCode;
        OnPropertyChanged(nameof(HasNoMsiProductCode));
    }

    /// <summary>Product code of the staged MSI, used to pre-fill MSI detection.</summary>
    private string _msiProductCode = "";

    public List<string> DetectionMethods { get; } = DetectionMethod.All;
    public IReadOnlyList<string> RegistryHives { get; } = RegistryHiveNames.All;
    public ObservableCollection<string> Operators { get; } =
        new() { "greaterThanOrEqual", "equal", "greaterThan", "lessThan", "lessThanOrEqual" };

    private string _newRuleMethod = DetectionMethod.FileExists;
    public string NewRuleMethod
    {
        get => _newRuleMethod;
        set
        {
            if (!Set(ref _newRuleMethod, value)) return;
            if (IsMsiMethod && string.IsNullOrWhiteSpace(_newRuleProductCode))
                NewRuleProductCode = _msiProductCode;
            OnPropertyChanged(nameof(IsFileMethod));
            OnPropertyChanged(nameof(IsFileVersionMethod));
            OnPropertyChanged(nameof(IsRegistryMethod));
            OnPropertyChanged(nameof(IsMsiMethod));
            OnPropertyChanged(nameof(HasNoMsiProductCode));
            AddRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsFileMethod => _newRuleMethod is DetectionMethod.FileExists or DetectionMethod.FileVersion;
    public bool IsFileVersionMethod => _newRuleMethod == DetectionMethod.FileVersion;
    public bool IsRegistryMethod => _newRuleMethod == DetectionMethod.RegistryKey;
    public bool IsMsiMethod => _newRuleMethod == DetectionMethod.MsiProductCode;

    /// <summary>MSI detection selected but no product code was found.</summary>
    public bool HasNoMsiProductCode => IsMsiMethod && string.IsNullOrWhiteSpace(_msiProductCode);

    private string _newRulePath = "";
    public string NewRulePath { get => _newRulePath; set { if (Set(ref _newRulePath, value)) AddRuleCommand.RaiseCanExecuteChanged(); } }

    private string _newRuleName = "";
    public string NewRuleName { get => _newRuleName; set => Set(ref _newRuleName, value); }

    private string _newRuleOperator = "greaterThanOrEqual";
    public string NewRuleOperator { get => _newRuleOperator; set => Set(ref _newRuleOperator, value); }

    private string _newRuleValue = "";
    public string NewRuleValue { get => _newRuleValue; set => Set(ref _newRuleValue, value); }

    private string _newRuleHive = RegistryHiveNames.LocalMachine;
    public string NewRuleHive { get => _newRuleHive; set => Set(ref _newRuleHive, value); }

    private string _newRuleKeyPath = "";
    public string NewRuleKeyPath { get => _newRuleKeyPath; set { if (Set(ref _newRuleKeyPath, value)) AddRuleCommand.RaiseCanExecuteChanged(); } }

    private string _newRuleValueName = "";
    public string NewRuleValueName { get => _newRuleValueName; set => Set(ref _newRuleValueName, value); }

    private string _newRuleProductCode = "";
    public string NewRuleProductCode
    {
        get => _newRuleProductCode;
        set { if (Set(ref _newRuleProductCode, value)) AddRuleCommand.RaiseCanExecuteChanged(); }
    }

    public bool CanAddRule => _newRuleMethod switch
    {
        DetectionMethod.RegistryKey => !string.IsNullOrWhiteSpace(NewRuleKeyPath),
        DetectionMethod.MsiProductCode => !string.IsNullOrWhiteSpace(NewRuleProductCode),
        _ => !string.IsNullOrWhiteSpace(NewRulePath),
    };

    private void AddDetectionRule()
    {
        switch (_newRuleMethod)
        {
            case DetectionMethod.RegistryKey:
                DetectionRules.Add(new DetectionRule
                {
                    Type = DetectionRuleType.Registry,
                    Path = RegistryHiveNames.Combine(NewRuleHive, NewRuleKeyPath),
                    FileOrFolderName = NewRuleValueName.Trim(),
                    DetectionType = "exists",
                });
                NewRuleKeyPath = "";
                NewRuleValueName = "";
                break;

            case DetectionMethod.MsiProductCode:
                DetectionRules.Add(new DetectionRule
                {
                    Type = DetectionRuleType.MSI,
                    Path = NewRuleProductCode.Trim(),
                });
                break;

            default:
                var checkVersion = _newRuleMethod == DetectionMethod.FileVersion;
                DetectionRules.Add(new DetectionRule
                {
                    Type = DetectionRuleType.File,
                    Path = NewRulePath.Trim(),
                    FileOrFolderName = NewRuleName.Trim(),
                    CheckVersion = checkVersion,
                    DetectionType = checkVersion ? "version" : "exists",
                    Operator = checkVersion ? NewRuleOperator : "",
                    DetectionValue = checkVersion ? NewRuleValue.Trim() : "",
                    Check32BitOn64System = true,
                });
                NewRulePath = "";
                NewRuleName = "";
                NewRuleValue = "";
                break;
        }
    }

    // ── Assignment groups ───────────────────────────────
    /// <summary>Shared group picker; each group carries its own intent.</summary>
    public GroupPickerViewModel GroupPicker { get; } = new();

    // ── Publishing ──────────────────────────────────────
    public ObservableCollection<PublishStepViewModel> PublishSteps { get; }

    private bool _isPublishing;
    public bool IsPublishing
    {
        get => _isPublishing;
        private set
        {
            if (!Set(ref _isPublishing, value)) return;
            OnPropertyChanged(nameof(IsNotPublishing));
            OnPropertyChanged(nameof(IsRunning));
            CancelUploadCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsNotPublishing => !_isPublishing;

    /// <summary>Publish started but not finished. Drives the spinner.</summary>
    public bool IsRunning => _isPublishing && !_isComplete;

    private string _publishTitle = "";
    public string PublishTitle { get => _publishTitle; private set => Set(ref _publishTitle, value); }

    private string _resultText = "";
    public string ResultText { get => _resultText; private set => Set(ref _resultText, value); }

    private bool _isComplete;
    public bool IsComplete
    {
        get => _isComplete;
        private set
        {
            if (!Set(ref _isComplete, value)) return;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsSucceeded));
            OnPropertyChanged(nameof(IsFailed));
            CancelUploadCommand.RaiseCanExecuteChanged();
        }
    }

    private bool _succeeded;
    public bool IsSucceeded => _isComplete && _succeeded;
    public bool IsFailed => _isComplete && !_succeeded;

    public bool UploadEnabled =>
        IsValidated && _auth.IsSignedIn && !IsPublishing &&
        !string.IsNullOrWhiteSpace(_settings.Settings.NetworkPaths.IntuneWinAppUtil);

    private async Task UploadAsync()
    {
        if (!UploadEnabled) return;

        var settings = _settings.Settings;
        var appInfo = new ApplicationInfo
        {
            Name = AppName.Trim(),
            Manufacturer = string.IsNullOrWhiteSpace(Manufacturer) ? "Unknown" : Manufacturer.Trim(),
            Version = string.IsNullOrWhiteSpace(Version) ? "1.0.0" : Version.Trim(),
            SourcesPath = PackageRoot,
            InstallContext = InstallContext,
            DisplayName = IntuneDisplayName,
        };

        NativeCodeSigner? signer = null;
        if (settings.CodeSigning.Enabled)
            signer = new NativeCodeSigner(settings.CodeSigning.CertificateThumbprint, settings.CodeSigning.TimestampServer);

        PublishSteps[1] = new PublishStepViewModel(2, $"Uploading to {TenantName} tenant");
        foreach (var s in PublishSteps) s.State = "pending";

        PublishTitle = $"Publishing {DisplayTitle}…";
        ResultText = "";
        _succeeded = false;
        IsComplete = false;
        IsPublishing = true;
        UploadCommand.RaiseCanExecuteChanged();
        PublishSteps[0].State = "working";

        var progress = new StepProgress(this);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var uploadService = new IntuneUploadService(
                _auth.GetAccessTokenAsync, signer, settings.NetworkPaths.IntuneWinAppUtil);

            var groups = GroupPicker.AssignableGroups;

            var appId = await Task.Run(() => uploadService.UploadWin32ApplicationAsync(
                appInfo,
                PackageRoot,
                DetectionRules.ToList(),
                settings.IntuneDefaults.InstallCommand,
                settings.IntuneDefaults.UninstallCommand,
                appInfo.DisplayName,
                appInfo.InstallContext,
                null,
                progress,
                requirements: settings.IntuneDefaults.Requirements,
                returnCodes: settings.IntuneDefaults.ReturnCodes,
                privacyUrl: settings.IntuneDefaults.PrivacyUrl,
                informationUrl: settings.IntuneDefaults.InformationUrl,
                pickedGroups: groups,
                ct: _cts!.Token));

            MarkDone(0); MarkDone(1); MarkDone(2); MarkDone(3);

            ResultText = groups.Count > 0
                ? $"Published and assigned to {groups.Count} group(s). App ID {appId}"
                : $"Published successfully. App ID {appId}";
            _succeeded = true;
            IsComplete = true;
        }
        catch (OperationCanceledException)
        {
            var cancelled = PublishSteps.FirstOrDefault(s => s.State == "working");
            if (cancelled != null) cancelled.State = "error";
            ResultText = "Upload cancelled.";
            _succeeded = false;
            IsComplete = true;
        }
        catch (Exception ex)
        {
            var working = PublishSteps.FirstOrDefault(s => s.State == "working");
            if (working != null) working.State = "error";
            ResultText = $"Upload failed: {ex.Message}";
            _succeeded = false;
            IsComplete = true;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            UploadCommand.RaiseCanExecuteChanged();
        }
    }

    private void MarkDone(int index)
    {
        if (PublishSteps[index].State != "done")
            PublishSteps[index].State = "done";
    }

    /// <summary>Maps 0-100 progress onto the first three overlay steps.</summary>
    private void OnUploadProgress(int pct)
    {
        if (pct < 30)
        {
            PublishSteps[0].State = "working";
        }
        else if (pct < 90)
        {
            MarkDone(0);
            if (PublishSteps[1].State == "pending") PublishSteps[1].State = "working";
        }
        else
        {
            MarkDone(0); MarkDone(1);
            if (PublishSteps[2].State == "pending") PublishSteps[2].State = "working";
        }
    }

    private void ResetAfterPublish()
    {
        IsPublishing = false;
        IsComplete = false;
        if (_succeeded)
        {
            // Clear the form for the next package.
            IsValidated = false;
            PackageRoot = "";
            PackageFolderName = "";
            AppName = Manufacturer = Version = SizeText = "";
            DetectionRules.Clear();
            GroupPicker.SelectedGroups.Clear();
            _msiProductCode = "";
            NewRuleProductCode = "";
            GroupPicker.SelectedGroups.Clear();
            OnPropertyChanged(nameof(DisplayTitle));
        }
        UploadCommand.RaiseCanExecuteChanged();
    }

    // ── Commands ────────────────────────────────────────
    public RelayCommand AddRuleCommand { get; }
    public RelayCommand<DetectionRule> RemoveRuleCommand { get; }
    public AsyncRelayCommand UploadCommand { get; }

    /// <summary>Stops an upload; the service removes the half-built app.</summary>
    public RelayCommand CancelUploadCommand { get; }
    public RelayCommand DoneCommand { get; }

    // ── Helpers ─────────────────────────────────────────



    private sealed class StepProgress : IUploadProgress
    {
        private readonly UploadToIntuneViewModel _vm;
        public StepProgress(UploadToIntuneViewModel vm) => _vm = vm;

        public void UpdateProgress(int percentage, string message)
            => Application.Current?.Dispatcher.Invoke(() => _vm.OnUploadProgress(percentage));
    }
}
