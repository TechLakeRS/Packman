using Packman.Services;
using Packman.ViewModels;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Packman.Views;

public partial class StepTest : UserControl
{
    private INotifyCollectionChanged? _lines;

    public StepTest()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookAutoScroll();
    }

    /// <summary>Keeps the console pinned to the newest line as output arrives.</summary>
    private void HookAutoScroll()
    {
        if (_lines != null) _lines.CollectionChanged -= OnLinesChanged;
        _lines = (DataContext as RemoteTestViewModel)?.Lines;
        if (_lines != null) _lines.CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) ConsoleScroll.ScrollToEnd();
    }

    /// <summary>Picks a package built earlier, for Remote Test opened from the rail.</summary>
    private void BrowsePackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select the PSADT package folder to test" };

        var intuneApps = AppServices.Settings.Settings.NetworkPaths.IntuneApplications;
        if (!string.IsNullOrEmpty(intuneApps) && Directory.Exists(intuneApps))
            dialog.InitialDirectory = intuneApps;

        if (dialog.ShowDialog() == true)
            (DataContext as RemoteTestViewModel)?.SelectPackage(dialog.FolderName);
    }
}
