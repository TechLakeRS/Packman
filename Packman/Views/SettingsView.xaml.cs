using Packman.Services;
using Packman.ViewModels;
using System.Windows.Controls;

namespace Packman.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(AppServices.Settings, AppServices.Auth);
    }
}
