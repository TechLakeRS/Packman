#if WINDOWS_DESKTOP_TESTS
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Packman.Models;
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
                vm.Upload.IntuneDisplayName = "Contoso Reader 4.2.0";
                vm.Upload.DetectionProductCode = "{3FA85F64-5717-4562-B3FC-2C963F66AFA6}";
                vm.Upload.SelectedDetectionMethod = DetectionMethod.MsiProductCode;
                vm.Upload.RefreshReview();
                var detail = new ApplicationDetailViewModel(new IntuneApplication
                {
                    DisplayName = "Contoso Reader", Publisher = "Contoso", Version = "4.2.0",
                    PublishingState = "published", Id = "sample-app", LastModified = DateTime.UtcNow
                });
                detail.Detail.InstallCommand = "Invoke-AppDeployToolkit.exe Install -DeployMode Silent";
                detail.Detail.UninstallCommand = "Invoke-AppDeployToolkit.exe Uninstall -DeployMode Silent";
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
                    shellContent.DataContext = shell.DataContext;
                    Render(shellContent, $"{theme}-shell", 1320, 820);
                    shell.Close();
                    Render(new StepGenerate { DataContext = vm }, $"{theme}-package", 920, 900);
                    var configure = new StepUpload { DataContext = vm };
                    Render(configure, $"{theme}-configure", 920, 1200);
                    ((System.Windows.Controls.Primitives.ToggleButton)configure.FindName("RequirementsToggle")).IsChecked = true;
                    Render(configure, $"{theme}-requirements", 920, 1500);
                    Render(new StepReview { DataContext = vm }, $"{theme}-review", 920, 1350);
                    Render(new RemoteTestView(), $"{theme}-remote-test", 980, 850);
                    Render(new ApplicationsView(), $"{theme}-applications", 980, 760);
                    Render(new UploadToIntuneView(), $"{theme}-existing-package", 980, 1200);
                    Render(new AdvancedView(), $"{theme}-directory-tools", 980, 760);
                    foreach (var name in new[] { "TabAuth", "TabNetworkPaths", "TabIntuneDefaults", "TabGroupAssignment", "TabCodeSign", "TabAppearance" })
                    {
                        ((RadioButton)settings.FindName(name)).IsChecked = true;
                        Render(settings, $"{theme}-settings-{name}", 980, 900);
                    }
                    foreach (var name in new[] { "overview", "package", "deployment" })
                    {
                        detail.Tab = name;
                        Render(new ApplicationDetailView { DataContext = detail }, $"{theme}-detail-{name}", 980, 1150);
                    }
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
