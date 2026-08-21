using Packman.Models;
using Packman.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Packman.Views;

public partial class AdvancedView : UserControl
{
    public AdvancedViewModel ViewModel { get; }

    /// <summary>Raised on "connect"; the host switches to Settings.</summary>
    public event Action? ConnectRequested;

    public AdvancedView()
    {
        ViewModel = new AdvancedViewModel();
        ViewModel.ConnectRequested += () => ConnectRequested?.Invoke();
        DataContext = ViewModel;
        InitializeComponent();
    }

    /// <summary>Re-reads sign-in state. Called each time the screen is shown.</summary>
    public void Refresh() => ViewModel.Refresh();

    private void BulkGroupResult_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is EntraGroup group) ViewModel.SelectBulkGroup(group);
    }

    private void DeviceResult_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is EntraDevice device) ViewModel.SelectDeviceResult(device);
    }

    private void AppGroupResult_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is EntraGroup group) ViewModel.SelectAppGroup(group);
    }
}
