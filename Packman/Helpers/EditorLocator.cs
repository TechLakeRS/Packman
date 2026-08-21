using System.IO;

namespace Packman.Helpers;

public static class EditorLocator
{
    public static string? FindVSCodePath()
    {
        string[] paths = {
            @"C:\Program Files\Microsoft VS Code\Code.exe",
            @"C:\Program Files (x86)\Microsoft VS Code\Code.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Microsoft VS Code\Code.exe")
        };

        foreach (var path in paths)
            if (File.Exists(path)) return path;

        return null;
    }

    public static string? FindPowerShellISEPath()
    {
        string[] paths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\PowerShell_ISE.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"System32\WindowsPowerShell\v1.0\PowerShell_ISE.exe")
        };

        foreach (var path in paths)
            if (File.Exists(path)) return path;

        return null;
    }
}
