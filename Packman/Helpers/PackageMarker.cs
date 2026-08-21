using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Packman.Helpers;

/// <summary>Stores the Intune app id next to a package, for supersedence on upgrade.</summary>
public static class PackageMarker
{
    private const string MarkerFileName = ".intune-appid.json";

    private class Marker
    {
        public string IntuneAppId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Version { get; set; } = "";
        public string Updated { get; set; } = "";
    }

    public static bool SaveMarker(string folderPath, string appId, string? displayName = null, string? version = null)
    {
        if (string.IsNullOrEmpty(folderPath) || string.IsNullOrEmpty(appId) || !Directory.Exists(folderPath))
            return false;

        try
        {
            var marker = new Marker
            {
                IntuneAppId = appId,
                DisplayName = displayName ?? "",
                Version = version ?? "",
                Updated = DateTime.UtcNow.ToString("o")
            };
            var json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(folderPath, MarkerFileName), json);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving app-id marker: {ex.Message}");
            return false;
        }
    }

    public static string? GetMarkerAppId(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return null;

        try
        {
            var path = Path.Combine(folderPath, MarkerFileName);
            if (!File.Exists(path))
                return null;

            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(path));
            return string.IsNullOrEmpty(marker?.IntuneAppId) ? null : marker.IntuneAppId;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading app-id marker: {ex.Message}");
            return null;
        }
    }
}
