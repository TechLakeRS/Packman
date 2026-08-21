namespace Packman.Helpers;

/// <summary>
/// Escapes values written into generated PowerShell. Metadata comes from MSI tables and
/// version resources, so vendor names like "O'Reilly" reach here unfiltered.
/// </summary>
public static class PowerShellLiteral
{
    /// <summary>Body of a single-quoted string.</summary>
    public static string SingleQuoted(string? value) => (value ?? "").Replace("'", "''");

    /// <summary>Body of a double-quoted string.</summary>
    public static string DoubleQuoted(string? value) => (value ?? "")
        .Replace("`", "``")
        .Replace("\"", "`\"")
        .Replace("$", "`$");
}
