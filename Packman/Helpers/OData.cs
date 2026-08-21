namespace Packman.Helpers;

/// <summary>Escaping for OData $filter values.</summary>
public static class OData
{
    /// <summary>Body of a single-quoted OData literal.</summary>
    public static string Literal(string? value) => (value ?? "").Replace("'", "''");
}
