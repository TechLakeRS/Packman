using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Packman.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Packman.Services;

/// <summary>
/// Swaps the palette dictionary at runtime. Every brush resolves with DynamicResource,
/// so the live window restyles without a restart. WPF UI's theme is switched alongside
/// it so the Fluent controls and the Mica backdrop follow.
/// </summary>
public static class ThemeService
{
    /// <summary>Packman amber, handed to WPF UI so Fluent controls match.</summary>
    private static readonly Color Accent = Color.FromRgb(0xE0, 0xA0, 0x50);

    // Absolute pack URIs: a relative Source resolves against the caller's context,
    // which is not the application root once this runs from a view model.
    private const string Dark = "pack://application:,,,/Themes/DarkTheme.xaml";
    private const string Light = "pack://application:,,,/Themes/LightTheme.xaml";

    /// <summary>True while the dark palette is loaded. Read by hosts that theme non-WPF surfaces.</summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>Raised after the palette swapped, so surfaces outside WPF (Monaco) can follow.</summary>
    public static event Action? Changed;

    public static void Apply(AppTheme theme)
    {
        var light = theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => SystemPrefersLight(),
        };
        var wanted = light ? Light : Dark;
        var wasDark = IsDark;
        IsDark = !light;

        // WPF UI first: applying its theme resets the accent, so amber goes back after.
        var uiTheme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(uiTheme, WindowBackdropType.Mica, updateAccent: false);
        ApplicationAccentColorManager.Apply(Accent, uiTheme, systemGlassColor: false, systemAccentColor: false);

        var merged = Application.Current?.Resources.MergedDictionaries;
        if (merged is null) return;

        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString ?? "";
            var isPalette = source.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)
                         || source.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase);
            if (!isPalette) continue;
            // Compare on file name: App.xaml declares a relative source, later swaps
            // use absolute pack URIs.
            var wantedFile = wanted[(wanted.LastIndexOf('/') + 1)..];
            if (source.EndsWith(wantedFile, StringComparison.OrdinalIgnoreCase)) return;

            merged[i] = new ResourceDictionary { Source = new Uri(wanted, UriKind.Absolute) };
            break;
        }

        if (wasDark != IsDark) Changed?.Invoke();
    }

    /// <summary>Windows stores the app-theme preference as a DWORD; unreadable means dark.</summary>
    private static bool SystemPrefersLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch { return false; }
    }
}
