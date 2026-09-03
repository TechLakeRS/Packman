using Packman.Helpers;
using Packman.ViewModels;
using System.Windows.Controls;

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
        var folder = PackageFolderDialog.Show("Select the built PSADT package folder");
        if (folder != null) ViewModel.ProcessSelectedFolder(folder);
    }
}
