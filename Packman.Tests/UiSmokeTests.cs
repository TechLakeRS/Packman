#if WINDOWS_DESKTOP_TESTS
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Packman.Models;
using Packman.Services;
using Packman.ViewModels;
using Packman.Views;
using Packman.Views.Controls;
using Xunit;

namespace Packman.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    public void Screens_and_editable_dropdown_render_in_both_themes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                var vm = new MainViewModel();
                vm.CreatePackage.AppName = "Contoso Reader";
                vm.CreatePackage.Manufacturer = "Contoso";
                vm.CreatePackage.Version = "4.2.0";
                vm.CreatePackage.SourcesPath = @"C:\Sources\ContosoReader-4.2.0-x64.msi";
                vm.CreatePackage.CurrentMsiInfo = new MsiInfoService.MsiInfo
                {
                    ProductCode = "{3FA85F64-5717-4562-B3FC-2C963F66AFA6}", ProductVersion = "4.2.0"
                };
                var detail = new ApplicationDetailViewModel(new IntuneApplication
                {
                    DisplayName = "Contoso Reader", Publisher = "Contoso", Version = "4.2.0",
                    PublishingState = "published", Id = "sample-app", LastModified = DateTime.UtcNow
                });
                detail.Detail.InstallCommand = "Invoke-AppDeployToolkit.exe Install -DeployMode Silent";
                detail.Detail.UninstallCommand = "Invoke-AppDeployToolkit.exe Uninstall -DeployMode Silent";
                detail.Detail.RestartBehavior = "basedOnReturnCode";
                detail.Detail.MaxRunTimeMinutes = 60;
                detail.Detail.MinDiskSpaceMB = 500;
                detail.Detail.Size = 84 * 1024 * 1024;
                detail.Detail.Description = "PDF reader for managed Windows devices. Sample application for layout verification.";
                detail.Detail.Statistics = new InstallationStatistics
                {
                    TotalDevices = 120, SuccessfulInstalls = 104, PendingInstalls = 8, FailedInstalls = 3,
                    NotInstalled = 3, NotApplicable = 2
                };
                var rule = DetectionRuleFactory.FileVersion(@"%ProgramFiles%\Contoso Reader", "Reader.exe", "4.2.0");
                detail.Detail.DetectionRules.Add(rule);
                detail.DetectionDisplays.Add(DetectionRuleDisplay.From(rule));
                var assignment = new AssignedGroup
                {
                    GroupId = "sample-group", GroupName = "Workplace engineering — Windows application pilot devices",
                    AssignmentType = "required"
                };
                detail.Detail.AssignedGroups.Add(assignment);
                var settings = new SettingsView();
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/Packman;component/Themes/{theme}Theme.xaml")
                    });
                    var shell = new MainWindow();
                    // Layout only: no window is shown, no sign-in or Graph operation is started.
                    var shellContent = (FrameworkElement)shell.Content;
                    shell.Content = null;
                    vm.CreatePackage.CurrentPackagePath = "";
                    shellContent.DataContext = vm;
                    Render(shellContent, $"{theme}-shell", 1320, 820);
                    shell.Close();
                    var package = new StepGenerate { DataContext = vm };
                    Render(package, $"{theme}-package", 920, 900);
                    Assert.True(((Border)package.FindName("SourceFields")).IsEnabled);
                    // A non-existent local sample path is sufficient for summaries; no generation or upload runs.
                    vm.CreatePackage.CurrentPackagePath = Path.Combine(Path.GetTempPath(), "Packman-UI-sample", "Contoso_Reader", "4.2.0");
                    Render(package, $"{theme}-package-generated", 920, 1150);
                    Assert.False(((Border)package.FindName("SourceFields")).IsEnabled);
                    vm.Upload.RefreshFromPackage();
                    vm.Upload.SelectedDeployMode = "Silent";
                    vm.Upload.GroupPicker.SelectedGroups.Clear();
                    vm.Upload.GroupPicker.SelectedGroups.Add(assignment);
                    vm.Upload.RefreshReview();
                    var configure = new StepUpload { DataContext = vm };
                    Render(configure, $"{theme}-configure", 920, 1200);
                    ((System.Windows.Controls.Primitives.ToggleButton)configure.FindName("RequirementsToggle")).IsChecked = true;
                    Render(configure, $"{theme}-requirements", 920, 1500);
                    Render(new StepReview { DataContext = vm }, $"{theme}-review", 920, 1350);
                    var remote = new RemoteTestView();
                    remote.ViewModel.TargetComputer = "TEST-PC-01";
                    remote.ViewModel.Lines.Add(new RemoteTestLine { Time = "09:00", Text = "Sample output — no deployment executed in this preview.", Kind = LineKind.Dim });
                    Render(remote, $"{theme}-remote-test", 980, 850);
                    Render(new ApplicationsView(), $"{theme}-applications", 980, 760);
                    Render(new UploadToIntuneView(), $"{theme}-existing-package", 980, 1200);
                    Render(new AdvancedView(), $"{theme}-directory-tools", 980, 760);
                    foreach (var name in new[] { "TabAuth", "TabNetworkPaths", "TabIntuneDefaults", "TabGroupAssignment", "TabCodeSign", "TabAppearance" })
                    {
                        ((RadioButton)settings.FindName(name)).IsChecked = true;
                        ((ScrollViewer)settings.FindName("SectionScroll")).ScrollToTop();
                        Render(settings, $"{theme}-settings-{name}", 980, 900);
                        if (name == "TabIntuneDefaults")
                        {
                            ((ScrollViewer)settings.FindName("SectionScroll")).ScrollToBottom();
                            Render(settings, $"{theme}-settings-return-codes", 980, 900);
                        }
                    }
                    var settingsVm = (SettingsViewModel)settings.DataContext;
                    settingsVm.CodeSigningEnabled = true;
                    settingsVm.IsAppRegistration = true;
                    settingsVm.CreateGroupPerPackage = true;
                    settingsVm.CreateUninstallGroupPerPackage = true;
                    foreach (var name in new[] { "TabAuth", "TabCodeSign", "TabGroupAssignment" })
                    {
                        ((RadioButton)settings.FindName(name)).IsChecked = true;
                        ((ScrollViewer)settings.FindName("SectionScroll")).ScrollToTop();
                        Render(settings, $"{theme}-settings-{name}-expanded", 980, 1100);
                    }
                    settingsVm.CodeSigningEnabled = false;
                    settingsVm.IsAppRegistration = false;
                    settingsVm.CreateGroupPerPackage = false;
                    settingsVm.CreateUninstallGroupPerPackage = false;
                    foreach (var name in new[] { "overview", "package", "deployment" })
                    {
                        detail.Tab = name;
                        Render(new ApplicationDetailView { DataContext = detail }, $"{theme}-detail-{name}", 980, 1150);
                    }
                    detail.DetectionDisplays[0].BeginEdit();
                    Render(new ApplicationDetailView { DataContext = detail }, $"{theme}-detail-edit-detection", 980, 1300);
                    detail.DetectionDisplays[0].CancelEdit();
                    var combo = new ComboBox { IsEditable = true, Text = "TEST-PC-01", Width = 240 };
                    Render(combo, $"{theme}-editable-dropdown", 320, 100);
                    var editor = Assert.IsType<TextBox>(combo.Template.FindName("PART_EditableTextBox", combo));
                    Assert.Equal(Visibility.Visible, editor.Visibility);
                    editor.Text = "TEST-PC-02";
                    Assert.Equal("TEST-PC-02", combo.Text);
                }
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "WPF layout smoke test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Choosing_an_exe_clears_previous_msi_detection_metadata()
    {
        var vm = new CreatePackageViewModel
        {
            CurrentMsiInfo = new MsiInfoService.MsiInfo { ProductCode = "{3FA85F64-5717-4562-B3FC-2C963F66AFA6}" }
        };
        // Missing source avoids icon extraction; selecting it must still discard stale MSI data.
        vm.LoadFromFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"));
        Assert.Null(vm.CurrentMsiInfo);
        Assert.True(string.IsNullOrEmpty(vm.BuildApplicationInfo().MsiProductCode));
    }

    private static void Render(FrameworkElement view, string name, int width, int height)
    {
        var surface = new Border
        {
            Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
            Padding = new Thickness(24), Child = view
        };
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
        Assert.True(view.ActualWidth > 0);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var directory = Path.Combine(AppContext.BaseDirectory, "UiPreviews");
        Directory.CreateDirectory(directory);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        png.Save(stream);
        surface.Child = null;
    }
}
#endif
