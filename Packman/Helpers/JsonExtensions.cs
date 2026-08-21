using System.Text.Json;

namespace Packman.Helpers;

/// <summary>Null-safe accessors for Graph JSON.</summary>
public static class JsonExtensions
{
    public static string GetSafeString(this JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static long GetSafeLong(this JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    public static int GetSafeInt(this JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    /// <summary>Reads an int from a report-row cell (number, or numeric string).</summary>
    public static int GetSafeInt(this JsonElement cell)
    {
        if (cell.ValueKind == JsonValueKind.Number && cell.TryGetInt32(out var n)) return n;
        if (cell.ValueKind == JsonValueKind.String && int.TryParse(cell.GetString(), out var s)) return s;
        return 0;
    }

    public static DateTime GetSafeDateTime(this JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            && DateTime.TryParse(v.GetString(), out var d) ? d : DateTime.MinValue;
}
