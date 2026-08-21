using Packman.Models;
using Packman.Services;
using Packman.ViewModels;
using System;
using System.Windows.Controls;

namespace Packman.Views;

public partial class ApplicationsView : UserControl
{
    public ApplicationsViewModel ViewModel { get; }

    /// <summary>Raised on row activation; the host swaps in the detail screen.</summary>
    public event Action<IntuneApplication>? AppOpened;

    /// <summary>Raised on "connect"; the host switches to Settings.</summary>
    public event Action? ConnectRequested;

    public ApplicationsView()
    {
        ViewModel = new ApplicationsViewModel();
        ViewModel.OpenRequested += a => AppOpened?.Invoke(a);
        ViewModel.ConnectRequested += () => ConnectRequested?.Invoke();
        DataContext = ViewModel;
        InitializeComponent();
    }

    /// <summary>Loads or refreshes the list. Called each time the screen is shown.</summary>
    public void Load() => ErrorReporter.FireAndForget(() => ViewModel.LoadAsync());
}
