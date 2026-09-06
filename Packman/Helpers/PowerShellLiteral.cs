using System.Management.Automation.Language;

namespace Packman.Helpers;

/// <summary>
/// Escapes values written into generated PowerShell. Metadata comes from MSI tables and
/// version resources, so vendor names like "O'Reilly" reach here unfiltered.
/// </summary>
public static class PowerShellLiteral
{
    /// <summary>Body of a single-quoted string.</summary>
    public static string SingleQuoted(string? value) => CodeGeneration.EscapeSingleQuotedStringContent(value ?? "");

    /// <summary>Body of a double-quoted string.</summary>
    public static string DoubleQuoted(string? value) => (value ?? "")
        .Replace("`", "``")
        .Replace("\"", "`\"")
        // PowerShell also recognizes typographic quotes as string delimiters.
        .Replace("\u201c", "`\u201c")
        .Replace("\u201d", "`\u201d")
        .Replace("\u201e", "`\u201e")
        .Replace("$", "`$");
}
