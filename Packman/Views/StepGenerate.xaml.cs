using Packman.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Packman.Views;

public partial class StepGenerate : UserControl
{
    private const string SystemHelpText = "Installs for all users with elevated privileges (most common for managed apps).";
    private const string UserHelpText = "Installs only for the current user, without requiring elevation.";

    public StepGenerate()
    {
        InitializeComponent();
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Application Source File",
            Filter = "Supported Files (*.msi;*.exe)|*.msi;*.exe|MSI Files (*.msi)|*.msi|EXE Files (*.exe)|*.exe|All Files (*.*)|*.*"
        };

        var browsePath = VM?.CreatePackage.SourcesPath;
        if (!string.IsNullOrEmpty(browsePath) && Directory.Exists(Path.GetDirectoryName(browsePath)))
            dialog.InitialDirectory = Path.GetDirectoryName(browsePath);

        if (dialog.ShowDialog() == true)
            VM?.CreatePackage.LoadFromFile(dialog.FileName);
    }

    private void SourcesPath_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void SourcesPath_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
                VM?.CreatePackage.LoadFromFile(files[0]);
        }
    }

    private void UploadExisting_Click(object sender, RoutedEventArgs e)
        => (Window.GetWindow(this) as MainWindow)?.NavigateToUploadIntune();

    private void SystemContext_Checked(object sender, RoutedEventArgs e)
    {
        if (VM != null) VM.CreatePackage.UserInstall = false;
        if (InstallContextHelpText != null) InstallContextHelpText.Text = SystemHelpText;
    }

    private void UserContext_Checked(object sender, RoutedEventArgs e)
    {
        if (VM != null) VM.CreatePackage.UserInstall = true;
        if (InstallContextHelpText != null) InstallContextHelpText.Text = UserHelpText;
    }

    private void CreateMode_Checked(object sender, RoutedEventArgs e)
    {
        if (VM != null) VM.IsUpgradeMode = false;
        if (CreateModeCard != null) CreateModeCard.Visibility = Visibility.Visible;
        if (UpgradeModeCard != null) UpgradeModeCard.Visibility = Visibility.Collapsed;
    }

    private void UpgradeMode_Checked(object sender, RoutedEventArgs e)
    {
        if (VM != null) VM.IsUpgradeMode = true;
        if (CreateModeCard != null) CreateModeCard.Visibility = Visibility.Collapsed;
        if (UpgradeModeCard != null) UpgradeModeCard.Visibility = Visibility.Visible;
    }

    private void BrowseUpgradePackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select existing PSADT package folder" };

        var intuneApps = VM?.SettingsService.Settings.NetworkPaths.IntuneApplications;
        if (!string.IsNullOrEmpty(intuneApps) && Directory.Exists(intuneApps))
            dialog.InitialDirectory = intuneApps;

        if (dialog.ShowDialog() == true)
            VM?.Upgrade.LoadPackage(dialog.FolderName);
    }

    private void BrowseUpgradeSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select New Source File",
            Filter = "Supported Files (*.msi;*.exe)|*.msi;*.exe|MSI Files (*.msi)|*.msi|EXE Files (*.exe)|*.exe|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
            VM?.Upgrade.SetNewSource(dialog.FileName);
    }
}
