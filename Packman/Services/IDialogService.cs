using System.Windows;

namespace Packman.Services;

/// <summary>
/// Prompts a view model needs to raise. The WPF implementation shows message boxes; a
/// test can answer them from code.
/// </summary>
public interface IDialogService
{
    /// <summary>Yes/No question; true for Yes.</summary>
    bool Confirm(string message, string title);

    /// <summary>Yes/No/Cancel question; true for Yes, false for No, null for Cancel.</summary>
    bool? ConfirmOrCancel(string message, string title);

    void Warn(string message, string title);
    void Info(string message, string title);
}

public sealed class MessageBoxDialogService : IDialogService
{
    public bool Confirm(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public bool? ConfirmOrCancel(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public void Warn(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Info(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
