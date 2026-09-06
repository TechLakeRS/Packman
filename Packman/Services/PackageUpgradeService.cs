using Packman.Helpers;
using System.Diagnostics;
using System.IO;

namespace Packman.Services;

/// <summary>
/// Rolls a PSADT v4 package to a new version: copy forward, swap the source file,
/// refresh the script metadata. No template is involved, so the packager's script
/// edits survive.
/// </summary>
public class PackageUpgradeService
{
    private readonly string _baseOutputPath;

    public PackageUpgradeService(string baseOutputPath) => _baseOutputPath = baseOutputPath;

    public Task<string> UpgradePackageAsync(
        string existingPackagePath,
        string newVersion,
        string newSourcesPath,
        CancellationToken cancellationToken = default)
        => Task.Run(() => UpgradePackage(existingPackagePath, newVersion, newSourcesPath, cancellationToken), cancellationToken);

    private string UpgradePackage(string existingPackagePath, string newVersion, string newSourcesPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existingScriptPath = PsadtScript.Find(existingPackagePath)
            ?? throw new FileNotFoundException(
                $"{PsadtLayout.ScriptName} not found in the existing package. Packman upgrades PSADT v4 packages only.");

        var existingApplication = Path.Combine(existingPackagePath, "Application");
        if (!Directory.Exists(existingApplication))
            throw new DirectoryNotFoundException($"Source Application folder not found: {existingApplication}");

        // Validate everything before creating the new folder, so a failure leaves nothing behind.
        if (!File.Exists(newSourcesPath))
            throw new FileNotFoundException($"Source file not found: {newSourcesPath}");

        var existingScript = PsadtScript.Load(existingScriptPath);
        var manufacturer = existingScript.Vendor;
        var appName = existingScript.AppName;
        if (string.IsNullOrWhiteSpace(appName))
            throw new InvalidOperationException("The existing script has no AppName, so the package folder cannot be derived.");

        var appFolderName = PackagePaths.AppFolderName(manufacturer, appName);
        var newPackagePath = PackagePaths.VersionFolder(_baseOutputPath, appFolderName, newVersion);

        if (Directory.Exists(newPackagePath))
            throw new InvalidOperationException(
                $"Version {newVersion} already exists for {appFolderName}. Delete it first or choose a different version.");

        var newIsMsi = Path.GetExtension(newSourcesPath).Equals(".msi", StringComparison.OrdinalIgnoreCase);
        var oldProductCode = existingScript.MsiProductCode ?? ReadProductCodeFromFiles(existingApplication);
        var newProductCode = newIsMsi ? ReadProductCode(newSourcesPath) : null;
        var newSourceFileName = Path.GetFileName(newSourcesPath);

        try
        {
            foreach (var folder in new[] { "Application", "Icon", "Intune" })
                Directory.CreateDirectory(Path.Combine(newPackagePath, folder));

            // Everything but the old installer comes across.
            DirectoryCopy.Copy(existingApplication, Path.Combine(newPackagePath, "Application"), cancellationToken, "Files");

            var files = Path.Combine(newPackagePath, "Application", "Files");
            Directory.CreateDirectory(files);
            File.Copy(newSourcesPath, Path.Combine(files, newSourceFileName), true);

            var icon = Path.Combine(existingPackagePath, "Icon");
            if (Directory.Exists(icon))
                DirectoryCopy.Copy(icon, Path.Combine(newPackagePath, "Icon"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            UpdateScript(newPackagePath, manufacturer, appName, newVersion, newSourceFileName, newIsMsi, oldProductCode, newProductCode);
            return newPackagePath;
        }
        catch
        {
            try { PackagePaths.DeleteInside(_baseOutputPath, newPackagePath); }
            catch (Exception cleanup) { Debug.WriteLine($"Could not remove the half-built package: {cleanup.Message}"); }
            throw;
        }
    }

    private static void UpdateScript(
        string packagePath, string manufacturer, string appName, string version,
        string newSourceFileName, bool newIsMsi, string? oldProductCode, string? newProductCode)
    {
        var scriptPath = Path.Combine(packagePath, "Application", PsadtLayout.ScriptName);
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"{PsadtLayout.ScriptName} not found: {scriptPath}");

        var script = PsadtScript.Load(scriptPath);
        script.StripSignatureBlock();

        script.Vendor = manufacturer;
        script.AppName = appName;
        script.AppVersion = version;
        script.ScriptDate = PsadtScript.TodayStamp;
        script.ScriptAuthor = Environment.UserName;

        script.ReplaceSourceFileName(newSourceFileName);

        if (!string.IsNullOrEmpty(newProductCode))
        {
            // Only the package's own product code moves; other GUIDs in the script stay.
            script.ReplaceProductCode(oldProductCode, newProductCode);
            script.ReplaceProductCode(PsadtScript.ProductCodePlaceholder, newProductCode);
        }
        else if (!newIsMsi)
        {
            // MSI to EXE: the old code no longer applies, flag it for the packager.
            script.ReplaceProductCode(oldProductCode, PsadtScript.ProductCodePlaceholder);
        }

        script.Save();
    }

    private static string? ReadProductCode(string msiPath)
    {
        try
        {
            var info = MsiInfoService.ExtractMsiInfo(msiPath);
            return info.IsValid && !string.IsNullOrEmpty(info.ProductCode) ? info.ProductCode : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not extract MSI product code from {msiPath}: {ex.Message}");
            return null;
        }
    }

    // Fallback when the script does not name its product code: the old installer itself.
    private static string? ReadProductCodeFromFiles(string applicationFolder)
    {
        var files = Path.Combine(applicationFolder, "Files");
        if (!Directory.Exists(files)) return null;

        return Directory.EnumerateFiles(files, "*.msi")
            .Select(ReadProductCode)
            .FirstOrDefault(code => code != null);
    }
}
