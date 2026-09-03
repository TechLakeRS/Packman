namespace Packman.Helpers;

/// <summary>One byte-count formatter for every size readout.</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>"1.4 MB", "—" for zero or negative.</summary>
    public static string Format(long bytes)
    {
        if (bytes <= 0) return "—";
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {Units[unit]}";
    }
}
