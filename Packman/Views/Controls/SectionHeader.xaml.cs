using System.Windows;
using System.Windows.Controls;

namespace Packman.Views.Controls;

/// <summary>The "LABEL ───── caption" row that opens every section.</summary>
public partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionHeader), new PropertyMetadata(""));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(SectionHeader), new PropertyMetadata(""));

    public SectionHeader() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Optional monospace note at the right end of the rule.</summary>
    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }
}
