using Packman.Services;
using System.IO;

namespace Packman.Helpers;

/// <summary>The "pick a package folder" dialog, opened on the IntuneApplications share when it is set.</summary>
public static class PackageFolderDialog
{
    /// <summary>The chosen folder, or null when the user cancelled.</summary>
    public static string? Show(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };

        var intuneApps = AppServices.Settings.Settings.NetworkPaths.IntuneApplications;
        if (!string.IsNullOrEmpty(intuneApps) && Directory.Exists(intuneApps))
            dialog.InitialDirectory = intuneApps;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
