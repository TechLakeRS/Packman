using Packman.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Packman.Views;

public partial class StepUpload : UserControl
{
    public StepUpload()
    {
        InitializeComponent();
    }

    private void GroupSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is not MainViewModel vm) return;

        var command = vm.Upload.GroupPicker.SearchGroupsCommand;
        if (command.CanExecute(null))
            command.Execute(null);
    }
}
