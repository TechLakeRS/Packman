using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Packman.Helpers;

/// <summary>Reads RequireAdmin out of a package's $adtSession hashtable.</summary>
public static class InstallContextParser
{
    /// <summary>"User" or "System"; defaults to "System".</summary>
    public static string ExtractFromPackage(string packagePath)
    {
        var scriptPath = Path.Combine(packagePath, "Application", PsadtLayout.ScriptName);
        if (!File.Exists(scriptPath))
            return "System";

        try
        {
            var match = Regex.Match(File.ReadAllText(scriptPath), @"^\s*RequireAdmin\s*=\s*\$(\w+)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            return match.Success && match.Groups[1].Value.Equals("false", StringComparison.OrdinalIgnoreCase)
                ? "User"
                : "System";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error extracting install context: {ex.Message}");
            return "System";
        }
    }
}
