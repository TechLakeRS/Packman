using Packman.Helpers;
using Packman.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Packman.ViewModels;

/// <summary>Which optional tool page is covering the wizard, if any.</summary>
public enum PackageTool { None, EditScript, RemoteTest }

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = AppServices.Settings;
    private readonly IntuneAuthService _auth = AppServices.Auth;

    public SettingsService SettingsService => _settingsService;

    public ObservableCollection<StepViewModel> Steps { get; }
    public CreatePackageViewModel CreatePackage { get; } = new();
    public UpgradePackageViewModel Upgrade { get; } = new();
    public UploadStepViewModel Upload { get; }
    public RemoteTestViewModel RemoteTest { get; }

    private const int GenerateStep = 0;
    private const int UploadStep = 1;
    private const int ReviewStep = 2;

    private bool _isUpgradeMode;
    public bool IsUpgradeMode
    {
        get => _isUpgradeMode;
        set { if (Set(ref _isUpgradeMode, value)) OnPropertyChanged(nameof(PrimaryLabel)); }
    }

    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand PrimaryCommand { get; }
    public RelayCommand<int> GoToStepCommand { get; }
    public RelayCommand OpenEditToolCommand { get; }
    public RelayCommand OpenTestToolCommand { get; }
    public RelayCommand CloseToolCommand { get; }
    public RelayCommand ContinueToUploadCommand { get; }
    public RelayCommand OpenPackageFolderCommand { get; }

    /// <summary>Clears the wizard so a second package can be built without restarting.</summary>
    public RelayCommand NewPackageCommand { get; }

    private int _currentStepIndex;
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        set
        {
            if (value < 0 || value >= Steps.Count) return;
            if (!Set(ref _currentStepIndex, value)) return;
            for (int i = 0; i < Steps.Count; i++)
            {
                Steps[i].IsCurrent = i == value;
                Steps[i].IsDone = i < value;
            }
            if (value == UploadStep) Upload.RefreshFromPackage();
            if (value == ReviewStep) Upload.RefreshReview();
            OnPropertyChanged(nameof(PrimaryLabel));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(StepPosition));
            OnPropertyChanged(nameof(ShowPrimaryKeyHint));
            BackCommand.RaiseCanExecuteChanged();
        }
    }

    // ── Optional tool pages (Edit Script / Remote Test) ─────────────────
    private PackageTool _activeTool = PackageTool.None;
    public PackageTool ActiveTool
    {
        get => _activeTool;
        private set
        {
            if (!Set(ref _activeTool, value)) return;
            OnPropertyChanged(nameof(IsWizard));
            OnPropertyChanged(nameof(IsToolOpen));
            OnPropertyChanged(nameof(IsEditToolOpen));
            OnPropertyChanged(nameof(IsTestToolOpen));
            OnPropertyChanged(nameof(ToolTitle));
            OnPropertyChanged(nameof(ToolSubtitle));
            OnPropertyChanged(nameof(ToolActionLabel));
        }
    }

    public bool IsWizard => _activeTool == PackageTool.None;
    public bool IsToolOpen => _activeTool != PackageTool.None;

    public bool IsEditToolOpen => _activeTool == PackageTool.EditScript;
    public bool IsTestToolOpen => _activeTool == PackageTool.RemoteTest;

    public string ToolTitle => _activeTool switch
    {
        PackageTool.EditScript => "Edit Script",
        PackageTool.RemoteTest => "Remote Test",
        _ => "",
    };

    public string ToolSubtitle => _activeTool switch
    {
        PackageTool.EditScript => "Edit the generated PSADT deployment script — completions come from the PSADT v4 catalog",
        PackageTool.RemoteTest => "Deploy the built package to a test machine and watch the result",
        _ => "",
    };

    public string ToolActionLabel => _activeTool switch
    {
        PackageTool.EditScript => "OPEN IN VS CODE",
        PackageTool.RemoteTest => "RUN INSTALL",
        _ => "",
    };

    // ── Package state surfaced to the Generate screen ───────────────────
    public bool HasPackage => !string.IsNullOrEmpty(CreatePackage.CurrentPackagePath);

    /// <summary>Trailing folder name, for the tool breadcrumb.</summary>
    public string PackageName
    {
        get
        {
            var path = CreatePackage.CurrentPackagePath;
            return string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        }
    }

    public string PackagePathShort => HasPackage ? CreatePackage.CurrentPackagePath : "no package yet";

    public string PrimaryLabel
    {
        get
        {
            if (CurrentStepIndex == GenerateStep)
            {
                if (HasPackage) return "CONTINUE TO UPLOAD";
                return IsUpgradeMode ? "UPGRADE PACKAGE" : "GENERATE PACKAGE";
            }
            if (CurrentStepIndex == UploadStep) return "CONTINUE TO REVIEW";
            if (Upload.IsFailed) return "RETRY UPLOAD";
            if (Upload.IsSucceeded) return "DONE";
            return "BUILD & UPLOAD";
        }
    }

    public bool IsLastStep => CurrentStepIndex == Steps.Count - 1;

    /// <summary>The ↵ hint hides while an upload is in flight.</summary>
    public bool ShowPrimaryKeyHint => !Upload.IsRunning;

    /// <summary>Swaps the primary action for a status line while an upload runs.</summary>
    public bool IsUploadRunning => Upload.IsRunning;

    public string StepPosition => $"step {CurrentStepIndex + 1} of {Steps.Count}";

    // ── Intune connection status (footer) ──────────────────────────────
    public bool IsConnected => _auth.IsSignedIn;
    public string ConnectionStatusText => _auth.IsSignedIn
        ? $"Connected to Microsoft Intune · {_auth.SignedInUser}"
        : "Not connected — sign in on the Settings page";

    public MainViewModel()
    {
        Upload = new UploadStepViewModel(CreatePackage, _settingsService, _auth);
        RemoteTest = new RemoteTestViewModel(_settingsService, CreatePackage, Upload);

        Steps = new ObservableCollection<StepViewModel>
        {
            new(GenerateStep, "Generate"),
            new(UploadStep,   "Upload"),
            new(ReviewStep,   "Review"),
        };

        BackCommand     = new RelayCommand(() => CurrentStepIndex--, () => CurrentStepIndex > 0);
        PrimaryCommand  = new AsyncRelayCommand(OnPrimaryAsync, () => !CreatePackage.IsGenerating && !Upgrade.IsBusy && !Upload.IsPublishing);
        GoToStepCommand = new RelayCommand<int>(i => CurrentStepIndex = i);

        OpenEditToolCommand      = new RelayCommand(() => ActiveTool = PackageTool.EditScript, () => HasPackage);
        OpenTestToolCommand      = new RelayCommand(() => ActiveTool = PackageTool.RemoteTest, () => HasPackage);
        CloseToolCommand         = new RelayCommand(() => ActiveTool = PackageTool.None);
        OpenPackageFolderCommand = new RelayCommand(OpenPackageFolder, () => HasPackage);
        NewPackageCommand        = new RelayCommand(StartNewPackage, () => HasPackage && !Upload.IsRunning);

        ContinueToUploadCommand = new RelayCommand(() =>
        {
            ActiveTool = PackageTool.None;
            CurrentStepIndex = UploadStep;
        });

        Steps[GenerateStep].IsCurrent = true;

        _auth.StateChanged += () =>
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionStatusText));
        };

        CreatePackage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CreatePackageViewModel.IsGenerating))
                PrimaryCommand.RaiseCanExecuteChanged();
            if (e.PropertyName == nameof(CreatePackageViewModel.CurrentPackagePath))
                RaisePackageDependents();
        };
        Upgrade.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpgradePackageViewModel.IsBusy))
                PrimaryCommand.RaiseCanExecuteChanged();
        };
        Upload.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UploadStepViewModel.IsPublishing))
                PrimaryCommand.RaiseCanExecuteChanged();

            // The publish button's label comes from the upload state.
            if (e.PropertyName is nameof(UploadStepViewModel.IsPublishing)
                              or nameof(UploadStepViewModel.IsRunning)
                              or nameof(UploadStepViewModel.IsComplete))
            {
                OnPropertyChanged(nameof(PrimaryLabel));
                OnPropertyChanged(nameof(ShowPrimaryKeyHint));
                OnPropertyChanged(nameof(IsUploadRunning));
                NewPackageCommand.RaiseCanExecuteChanged();
            }
        };
    }

    private void RaisePackageDependents()
    {
        OnPropertyChanged(nameof(HasPackage));
        OnPropertyChanged(nameof(PackageName));
        OnPropertyChanged(nameof(PackagePathShort));
        OnPropertyChanged(nameof(PrimaryLabel));
        OpenEditToolCommand.RaiseCanExecuteChanged();
        OpenTestToolCommand.RaiseCanExecuteChanged();
        OpenPackageFolderCommand.RaiseCanExecuteChanged();
        NewPackageCommand.RaiseCanExecuteChanged();
    }

    private void StartNewPackage()
    {
        ActiveTool = PackageTool.None;
        CreatePackage.Reset();
        Upgrade.Reset();
        CurrentStepIndex = GenerateStep;
        RaisePackageDependents();
    }

    private async Task OnPrimaryAsync()
    {
        if (CurrentStepIndex == GenerateStep)
        {
            // With a package present the button advances instead of regenerating.
            if (HasPackage)
            {
                CurrentStepIndex = UploadStep;
                return;
            }

            if (IsUpgradeMode)
                await RunUpgradeAsync();
            else
                await RunCreateAsync();
            return;
        }

        if (CurrentStepIndex == UploadStep)
        {
            CurrentStepIndex = ReviewStep;
            return;
        }

        await Upload.UploadAsync();
    }

    private async Task RunCreateAsync()
    {
        // Ask before replacing, rather than reporting the collision after the fact.
        var existing = CreatePackage.FindExistingPackage(_settingsService.Settings);
        var overwrite = false;
        if (existing != null)
        {
            var answer = MessageBox.Show(
                $"A package for this version already exists:\n\n{existing}\n\nReplace it?",
                "Package already exists", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            overwrite = true;
        }

        var packagePath = await CreatePackage.GenerateAsync(_settingsService.Settings, overwrite);
        if (!string.IsNullOrEmpty(packagePath))
            RaisePackageDependents();
        else if (!string.IsNullOrEmpty(CreatePackage.StatusText))
            MessageBox.Show(CreatePackage.StatusText, "Package Generation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task RunUpgradeAsync()
    {
        var newPackagePath = await Upgrade.UpgradeAsync(_settingsService.Settings);
        if (string.IsNullOrEmpty(newPackagePath))
        {
            if (!string.IsNullOrEmpty(Upgrade.StatusText))
                MessageBox.Show(Upgrade.StatusText, "Package Upgrade", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Carry the upgraded metadata across so the tools and publish step work.
        var meta = Upgrade.LoadedMetadata;
        if (meta != null)
        {
            CreatePackage.AppName = meta.AppName;
            CreatePackage.Manufacturer = meta.Manufacturer;
            CreatePackage.UserInstall = meta.InstallContext.Equals("User", StringComparison.OrdinalIgnoreCase);
        }
        CreatePackage.Version = Upgrade.NewVersion;
        CreatePackage.SourcesPath = Upgrade.NewSourcePath;
        CreatePackage.CurrentPackagePath = newPackagePath;
        CreatePackage.PredecessorAppId = PackageMarker.GetMarkerAppId(Upgrade.ExistingPackagePath) ?? "";

        RaisePackageDependents();
    }

    private void OpenPackageFolder()
    {
        var path = CreatePackage.CurrentPackagePath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open package folder: {ex.Message}");
        }
    }

}
