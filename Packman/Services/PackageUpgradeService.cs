using Packman.Helpers;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Packman.Services;

/// <summary>
/// Rolls a PSADT v4 package to a new version: copy forward, swap the source file,
/// refresh the script metadata.
/// </summary>
public class PackageUpgradeService
{
    private readonly string _baseOutputPath;

    // No template: copying the package forward keeps the packager's script edits.
    public PackageUpgradeService(string baseOutputPath) => _baseOutputPath = baseOutputPath;

    public async Task<string> UpgradePackageAsync(
        string existingPackagePath,
        string newVersion,
        string newSourcesPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var applicationFolder = Path.Combine(existingPackagePath, "Application");
            var existingScriptPath = FolderBrowserHelper.GetPSADTScriptPath(applicationFolder)
                ?? FolderBrowserHelper.GetPSADTScriptPath(existingPackagePath);

            if (string.IsNullOrEmpty(existingScriptPath) || !File.Exists(existingScriptPath))
                throw new FileNotFoundException(
                    $"{PsadtLayout.ScriptName} not found in the existing package. Packman upgrades PSADT v4 packages only.");

            var metadata = MetadataExtractor.ExtractMetadataFromScript(existingScriptPath);
            var manufacturer = metadata.GetValueOrDefault("Vendor", "");
            var appName = metadata.GetValueOrDefault("AppName", "");

            var appFolderName = $"{manufacturer.Replace(" ", "_")}_{appName.Replace(" ", "_")}";
            var newPackagePath = Path.Combine(_baseOutputPath, appFolderName, newVersion);

            if (Directory.Exists(newPackagePath))
                throw new InvalidOperationException($"Version {newVersion} already exists for {appFolderName}. Please delete it first or choose a different version.");

            Directory.CreateDirectory(newPackagePath);

            foreach (var folder in new[] { "Application", "Icon", "Intune" })
                Directory.CreateDirectory(Path.Combine(newPackagePath, folder));

            await CopyApplicationFolderAsync(existingPackagePath, newPackagePath, cancellationToken);

            var newSourceFileName = await CopyNewSourceFilesAsync(newSourcesPath, newPackagePath, cancellationToken);
            await CopyOptionalFolderAsync(existingPackagePath, newPackagePath, "Icon", cancellationToken);

            string newMsiProductCode = "";
            if (Path.GetExtension(newSourcesPath).Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var msiInfo = MsiInfoService.ExtractMsiInfo(newSourcesPath);
                    if (msiInfo.IsValid && !string.IsNullOrEmpty(msiInfo.ProductCode))
                        newMsiProductCode = msiInfo.ProductCode;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not extract MSI product code: {ex.Message}");
                }
            }

            await UpdateScriptForUpgradeAsync(newPackagePath, manufacturer, appName, newVersion, newSourceFileName, newMsiProductCode, cancellationToken);

            return newPackagePath;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error upgrading package: {ex.Message}", ex);
        }
    }

    private async Task CopyApplicationFolderAsync(string existingPackagePath, string newPackagePath, CancellationToken ct)
    {
        var sourceAppFolder = Path.Combine(existingPackagePath, "Application");
        var destAppFolder = Path.Combine(newPackagePath, "Application");

        if (!Directory.Exists(sourceAppFolder))
            throw new DirectoryNotFoundException($"Source Application folder not found: {sourceAppFolder}");

        await Task.Run(() => CopyDirectorySelective(sourceAppFolder, destAppFolder, new[] { "Files" }), ct);
    }

    private async Task CopyOptionalFolderAsync(string existingPackagePath, string newPackagePath, string folderName, CancellationToken ct)
    {
        try
        {
            var source = Path.Combine(existingPackagePath, folderName);
            if (!Directory.Exists(source))
                return;
            await Task.Run(() => CopyDirectory(source, Path.Combine(newPackagePath, folderName)), ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Warning: Could not copy {folderName} folder: {ex.Message}");
        }
    }

    private async Task<string> CopyNewSourceFilesAsync(string sourcePath, string packagePath, CancellationToken ct)
    {
        var applicationFilesPath = Path.Combine(packagePath, "Application", "Files");
        Directory.CreateDirectory(applicationFilesPath);

        string sourceFileName = "";
        await Task.Run(() =>
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Source file not found: {sourcePath}");
            sourceFileName = Path.GetFileName(sourcePath);
            File.Copy(sourcePath, Path.Combine(applicationFilesPath, sourceFileName), true);
        }, ct);

        return sourceFileName;
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
        foreach (var subDir in Directory.GetDirectories(sourceDir))
            CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
    }

    private void CopyDirectorySelective(string sourceDir, string destinationDir, string[] excludeFolders)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (excludeFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            CopyDirectorySelective(subDir, Path.Combine(destinationDir, name), excludeFolders);
        }
    }

    private async Task UpdateScriptForUpgradeAsync(
        string packagePath, string manufacturer, string appName, string version,
        string newSourceFileName, string newMsiProductCode, CancellationToken ct)
    {
        var scriptPath = Path.Combine(packagePath, "Application", PsadtLayout.ScriptName);
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"{PsadtLayout.ScriptName} not found: {scriptPath}");

        var scriptContent = await File.ReadAllTextAsync(scriptPath, ct);
        scriptContent = UpdateScriptMetadataV4(scriptContent, manufacturer, appName, version);
        scriptContent = UpdateSourceFilePaths(scriptContent, newSourceFileName);

        if (!string.IsNullOrEmpty(newMsiProductCode))
            scriptContent = UpdateMsiProductCodes(scriptContent, newMsiProductCode);
        else if (!Path.GetExtension(newSourceFileName).Equals(".msi", StringComparison.OrdinalIgnoreCase))
            scriptContent = ReplaceMsiProductCodesWithPlaceholder(scriptContent);

        await File.WriteAllTextAsync(scriptPath, scriptContent, ct);
    }

    private string UpdateScriptMetadataV4(string scriptContent, string manufacturer, string appName, string version)
    {
        var lines = scriptContent.Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("AppVendor") && line.Contains("="))
                lines[i] = $"    AppVendor = '{Q(manufacturer)}'";
            else if (line.StartsWith("AppName") && line.Contains("=") && !line.Contains("AppNameWithVersion"))
                lines[i] = $"    AppName = '{Q(appName)}'";
            else if (line.StartsWith("AppVersion") && line.Contains("="))
                lines[i] = $"    AppVersion = '{Q(version)}'";
            else if (line.StartsWith("AppScriptDate") && line.Contains("="))
                lines[i] = $"    AppScriptDate = '{DateTime.Now:yyyy-MM-dd}'";
            else if (line.StartsWith("AppScriptAuthor") && line.Contains("="))
                lines[i] = $"    AppScriptAuthor = '{Q(Environment.UserName)}'";
        }
        return string.Join('\n', lines);
    }

    private static string Q(string? value) => PowerShellLiteral.SingleQuoted(value);

    private string UpdateSourceFilePaths(string scriptContent, string newSourceFileName)
    {
        if (string.IsNullOrWhiteSpace(scriptContent))
            return scriptContent;

        var patterns = new List<(string pattern, string replacement)>
        {
            (@"(\$adtSession\.DirFiles\\|""\$adtSession\.DirFiles\\)([^""'\s\\]+\.(exe|msi|bat|cmd))", $"$1{newSourceFileName}"),
            (@"('\$adtSession\.DirFiles\\)([^""'\s\\]+\.msi)", $"$1{newSourceFileName}")
        };

        var result = scriptContent;
        foreach (var (pattern, replacement) in patterns)
            result = Regex.Replace(result, pattern, replacement, RegexOptions.IgnoreCase);

        return result;
    }

    private string UpdateMsiProductCodes(string scriptContent, string newProductCode)
    {
        if (string.IsNullOrWhiteSpace(scriptContent) || string.IsNullOrWhiteSpace(newProductCode))
            return scriptContent;

        var productCodePattern = @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}";
        return Regex.Replace(scriptContent, productCodePattern, newProductCode);
    }

    private string ReplaceMsiProductCodesWithPlaceholder(string scriptContent)
    {
        if (string.IsNullOrWhiteSpace(scriptContent))
            return scriptContent;

        var productCodePattern = @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}";
        return Regex.Replace(scriptContent, productCodePattern, "{PRODUCT-CODE-PLACEHOLDER}");
    }
}
