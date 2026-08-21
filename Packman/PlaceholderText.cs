using System.Windows;
using System.Windows.Automation;

namespace Packman;

public static class PlaceholderText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(PlaceholderText),
        new FrameworkPropertyMetadata(string.Empty, OnTextChanged));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);

    /// <summary>
    /// Filter and search boxes carry no label, so the placeholder doubles as the accessible
    /// name. An explicit AutomationProperties.Name still wins.
    /// </summary>
    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string placeholder || string.IsNullOrWhiteSpace(placeholder)) return;
        if (!string.IsNullOrEmpty(AutomationProperties.GetName(d))) return;

        AutomationProperties.SetName(d, placeholder);
    }
}
