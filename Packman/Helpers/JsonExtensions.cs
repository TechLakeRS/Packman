using System.Globalization;
using System.Text.Json;

namespace Packman.Helpers;

/// <summary>Null-safe accessors for Graph JSON. A missing or mistyped property reads as the default.</summary>
public static class JsonExtensions
{
    public static string GetSafeString(this JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    public static long GetSafeLong(this JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n : 0;

    public static int GetSafeInt(this JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : 0;

    public static bool GetSafeBool(this JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Reads an int from a report-row cell (number, or numeric string).</summary>
    public static int GetSafeInt(this JsonElement cell)
    {
        if (cell.ValueKind == JsonValueKind.Number && cell.TryGetInt32(out var n)) return n;
        if (cell.ValueKind == JsonValueKind.String && int.TryParse(cell.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        return 0;
    }

    /// <summary>Graph timestamps are ISO-8601 UTC; parsed culture-independently and kept as UTC.</summary>
    public static DateTime GetSafeDateTime(this JsonElement el, string prop)
    {
        var text = el.GetSafeString(prop);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime
            : DateTime.MinValue;
    }
}
