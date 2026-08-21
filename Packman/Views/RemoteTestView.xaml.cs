using Packman.Services;
using Packman.ViewModels;
using System.Windows.Controls;

namespace Packman.Views;

/// <summary>
/// Remote Test as its own screen, reached from the rail. Unlike the wizard's copy it
/// starts with no package, so the user picks one built earlier.
/// </summary>
public partial class RemoteTestView : UserControl
{
    public RemoteTestViewModel ViewModel { get; }

    public RemoteTestView()
    {
        ViewModel = new RemoteTestViewModel(AppServices.Settings);
        DataContext = ViewModel;
        InitializeComponent();
    }

    /// <summary>Picks up machines the wizard's Remote Test used.</summary>
    public void Refresh() => ViewModel.RefreshRecentComputers();
}
