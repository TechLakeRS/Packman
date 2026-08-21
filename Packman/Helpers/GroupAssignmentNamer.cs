namespace Packman.Helpers;

/// <summary>Expands %vendor%, %appName% and %appVersion% in a name template.</summary>
public static class GroupAssignmentNamer
{
    public static string Build(string template, string vendor, string appName, string version)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";
        return template
            .Replace("%vendor%", vendor, System.StringComparison.OrdinalIgnoreCase)
            .Replace("%appName%", appName, System.StringComparison.OrdinalIgnoreCase)
            .Replace("%appVersion%", version, System.StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
