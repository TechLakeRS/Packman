using System.Management.Automation.Language;

namespace Packman.Services;

/// <summary>A parse error in the 1-based coordinates Monaco expects.</summary>
public record SyntaxError(int Line, int Column, int EndLine, int EndColumn, string Message);

/// <summary>Parses only; nothing in the script is executed.</summary>
public static class PowerShellSyntaxValidator
{
    public static List<SyntaxError> Validate(string script)
    {
        Parser.ParseInput(script, out _, out var errors);

        return errors.Select(e =>
        {
            var line = e.Extent.StartLineNumber;
            var column = e.Extent.StartColumnNumber;
            var endLine = e.Extent.EndLineNumber;
            var endColumn = e.Extent.EndColumnNumber;

            // Monaco draws nothing for a zero-width extent.
            if (endLine == line && endColumn <= column) endColumn = column + 1;

            return new SyntaxError(line, column, endLine, endColumn, e.Message);
        }).ToList();
    }
}
