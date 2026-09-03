using Packman.Helpers;
using Packman.Models;
using Packman.Services;
using Packman.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Packman.Views;

public partial class ApplicationDetailView : UserControl
{
    private ApplicationDetailViewModel? _vm;

    /// <summary>Raised by the breadcrumb; the host returns to the list.</summary>
    public event Action? BackRequested;

    /// <summary>Raised after a retire; the host returns to the list and refreshes.</summary>
    public event Action? Deleted;

    /// <summary>Raised by "Update version"; the host switches to the Create/Upgrade flow.</summary>
    public event Action<IntuneApplication>? UpdateRequested;

    public ApplicationDetailView()
    {
        InitializeComponent();
    }

    /// <summary>Shows the app and starts the detail load.</summary>
    public void Show(IntuneApplication app)
    {
        _vm = new ApplicationDetailViewModel(app);
        DataContext = _vm;
        ErrorReporter.FireAndForget(_vm.LoadAsync);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void ViewInIntune_Click(object sender, RoutedEventArgs e) => _vm?.OpenInIntune();

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null) UpdateRequested?.Invoke(_vm.Detail);
    }

    private void ManageAssignments_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.Tab = "deployment";
    }

    // ── Clipboard ──

    private static void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch { /* clipboard can be locked by another process; not worth surfacing */ }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e) => CopyText(_vm?.SourcePath);
    private void CopyAppId_Click(object sender, RoutedEventArgs e) => CopyText(_vm?.Detail.Id);
    private void CopyInstall_Click(object sender, RoutedEventArgs e) => CopyText(_vm?.Detail.InstallCommand);
    private void CopyUninstall_Click(object sender, RoutedEventArgs e) => CopyText(_vm?.Detail.UninstallCommand);

    // ── Package source ──

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm?.SourcePath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditScript_Click(object sender, RoutedEventArgs e)
    {
        var script = _vm?.SourceScriptPath;
        if (string.IsNullOrEmpty(script) || !File.Exists(script))
        {
            MessageBox.Show("No PSADT script found in the package's source folder.", "Not found",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            EditorLocator.Open(script);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open script: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Detection rule editing ──

    private static T? RowContext<T>(object sender) where T : class =>
        (sender as FrameworkElement)?.DataContext as T;

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var type = ((NewRuleType.SelectedItem as ComboBoxItem)?.Content as string) switch
        {
            "MSI" => DetectionRuleType.MSI,
            "Registry" => DetectionRuleType.Registry,
            _ => DetectionRuleType.File,
        };
        _vm.AddDetectionRule(type);
    }

    private void EditRule_Click(object sender, RoutedEventArgs e) =>
        RowContext<DetectionRuleDisplay>(sender)?.BeginEdit();

    private void CancelRule_Click(object sender, RoutedEventArgs e)
    {
        var display = RowContext<DetectionRuleDisplay>(sender);
        if (display == null || _vm == null) return;
        if (display.IsNew)
            _vm.DiscardNewRule(display);
        else
            display.CancelEdit();
    }

    private void SaveRule_Click(object sender, RoutedEventArgs e)
    {
        var display = RowContext<DetectionRuleDisplay>(sender);
        if (display == null || _vm == null) return;
        display.ApplyEdit();
        ErrorReporter.FireAndForget(_vm.SaveDetectionRulesAsync);
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        var display = RowContext<DetectionRuleDisplay>(sender);
        if (display == null || _vm == null) return;

        var result = MessageBox.Show(
            "Delete this detection rule?\n\nIf no rule matches anymore, Intune considers the app not installed and required assignments will reinstall it.",
            "Delete detection rule", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        ErrorReporter.FireAndForget(() => _vm.DeleteDetectionRuleAsync(display));
    }

    // ── Assignments ──

    private void AddAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null) ErrorReporter.FireAndForget(_vm.AddAssignmentAsync);
    }

    private void GroupResult_Click(object sender, RoutedEventArgs e)
    {
        var group = RowContext<EntraGroup>(sender);
        if (group != null) _vm?.SelectGroupResult(group);
    }

    private void RemoveAssignment_Click(object sender, RoutedEventArgs e)
    {
        var group = RowContext<AssignedGroup>(sender);
        if (group == null || _vm == null) return;

        var result = MessageBox.Show(
            $"Remove the {group.StatusLabel} assignment for \"{group.GroupName}\"?",
            "Remove assignment", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        ErrorReporter.FireAndForget(() => _vm.RemoveAssignmentAsync(group));
    }

    // ── Group members slide-over ──

    private void GroupRow_Click(object sender, RoutedEventArgs e)
    {
        var group = RowContext<AssignedGroup>(sender);
        if (group != null && _vm != null) ErrorReporter.FireAndForget(() => _vm.OpenMembersAsync(group));
    }

    private void FlyoutClose_Click(object sender, RoutedEventArgs e) => _vm?.CloseFlyout();

    private void FlyoutBackdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _vm?.CloseFlyout();

    private void MemberResult_Click(object sender, RoutedEventArgs e)
    {
        var member = RowContext<GroupMember>(sender);
        if (member != null && _vm != null) ErrorReporter.FireAndForget(() => _vm.AddMemberAsync(member));
    }

    private void RemoveMember_Click(object sender, RoutedEventArgs e)
    {
        var member = RowContext<GroupMember>(sender);
        if (member == null || _vm == null) return;

        var result = MessageBox.Show(
            $"Remove \"{member.DisplayName}\" from \"{_vm.FlyoutGroup?.GroupName}\"?\n\nThis affects every app assigned to the group.",
            "Remove member", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        ErrorReporter.FireAndForget(() => _vm.RemoveMemberAsync(member));
    }

    private void Retire_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        var result = MessageBox.Show(
            $"Retire \"{_vm.Detail.DisplayName}\" from Intune?\n\nThis permanently removes the Win32 app from the tenant. This cannot be undone.",
            "Retire from Intune", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        ErrorReporter.FireAndForget(async () =>
        {
            if (await _vm.DeleteAsync()) Deleted?.Invoke();
        });
    }
}
