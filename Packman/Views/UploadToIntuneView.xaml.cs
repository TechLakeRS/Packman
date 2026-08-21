using Packman.Services;
using Packman.ViewModels;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;

namespace Packman.Views;

public partial class UploadToIntuneView : UserControl
{
    public UploadToIntuneViewModel ViewModel { get; }

    public UploadToIntuneView()
    {
        ViewModel = new UploadToIntuneViewModel();
        DataContext = ViewModel;
        InitializeComponent();
    }

    /// <summary>Called by the host when the page is shown, to refresh sign-in state.</summary>
    public void Refresh() => ViewModel.Refresh();

    private void Browse_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select the built PSADT package folder" };

        var intuneApps = AppServices.Settings.Settings.NetworkPaths.IntuneApplications;
        if (!string.IsNullOrEmpty(intuneApps) && Directory.Exists(intuneApps))
            dialog.InitialDirectory = intuneApps;

        if (dialog.ShowDialog() == true)
            ViewModel.ProcessSelectedFolder(dialog.FolderName);
    }

    private void GroupSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel.GroupPicker.SearchGroupsCommand.CanExecute(null))
            ViewModel.GroupPicker.SearchGroupsCommand.Execute(null);
    }
}
