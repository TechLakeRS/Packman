using Packman.Helpers;
using System.IO;

namespace Packman.Services;

public partial class IntuneUploadService
{
    /// <summary>
    /// Rebuilds the .intunewin from the package folder and publishes it as a new content
    /// version of an app that already exists in Intune. Nothing else on the app is touched:
    /// name, description, install commands, detection rules, requirements, return codes,
    /// icon and assignments stay exactly as the tenant has them.
    /// </summary>
    /// <returns>The committed content version id.</returns>
    public async Task<string> UpdatePackageContentAsync(
        string appId,
        string appName,
        string packagePath,
        string? version = null,
        IUploadProgress? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("No Intune app id to update.", nameof(appId));
        if (!Directory.Exists(packagePath))
            throw new DirectoryNotFoundException($"Package folder not found: {packagePath}");

        using var uploadLogger = new UploadLogger(string.IsNullOrWhiteSpace(appName) ? appId : appName, LogOperationType.Update);
        IntuneWinInfo? intuneWinInfo = null;

        try
        {
            uploadLogger.Section("UPDATE METADATA");
            uploadLogger.LogMetadata("Application Name", appName);
            uploadLogger.LogMetadata("Version", version ?? "");
            uploadLogger.LogMetadata("Intune App Id", appId);
            uploadLogger.LogMetadata("Package Path", packagePath);
            uploadLogger.Info("Only the package content is replaced; app metadata is left untouched.");
            uploadLogger.Info($"Update log file: {uploadLogger.LogFilePath}");

            uploadLogger.Section("UPDATE PROCESS");

            progress?.UpdateProgress(5, "Authenticating with Microsoft Graph...");
            uploadLogger.Progress(5, "Authenticating with Microsoft Graph...");
            _ = await _tokenProvider();
            uploadLogger.Success("Authentication successful");
            EnsureHttpClient();

            progress?.UpdateProgress(10, "Signing application files...");
            uploadLogger.Progress(10, "Signing application files...");
            await SignApplicationFilesAsync(packagePath, progress, uploadLogger, ct);

            progress?.UpdateProgress(20, "Regenerating .intunewin package...");
            uploadLogger.Progress(20, "Regenerating .intunewin package...");
            await CreateIntuneWinFileAsync(packagePath, ct);
            uploadLogger.Success("Package regenerated successfully");

            progress?.UpdateProgress(25, "Verifying package creation...");
            var intuneWinFile = FindNewestIntuneWinFile(packagePath)
                ?? throw new Exception("No .intunewin file found after conversion.");
            uploadLogger.Success($"Found .intunewin file: {Path.GetFileName(intuneWinFile)}");

            progress?.UpdateProgress(30, "Reading package metadata...");
            intuneWinInfo = ExtractIntuneWinInfo(intuneWinFile);
            uploadLogger.Success($"Package metadata extracted - Size: {intuneWinInfo.UnencryptedContentSize:N0} bytes");

            ct.ThrowIfCancellationRequested();

            progress?.UpdateProgress(45, "Preparing content storage...");
            var contentVersionId = await CreateContentVersionAsync(appId, ct);
            _currentAppId = appId;
            _currentContentVersionId = contentVersionId;
            uploadLogger.Success($"Content version created: {contentVersionId}");

            progress?.UpdateProgress(55, "Initializing file upload...");
            var fileId = await CreateFileEntryAsync(appId, contentVersionId, intuneWinInfo, ct);
            _currentFileId = fileId;
            uploadLogger.Success($"File entry created: {fileId}");

            progress?.UpdateProgress(65, "Requesting Azure upload URL...");
            var azureStorageInfo = await WaitForAzureStorageUriAsync(appId, contentVersionId, fileId, ct);
            uploadLogger.Success("Azure Storage URI obtained");

            progress?.UpdateProgress(75, "Uploading package to Azure...");
            await UploadFileToAzureStorageAsync(azureStorageInfo.SasUri, intuneWinInfo.EncryptedFilePath, progress, ct);
            uploadLogger.Success("Package uploaded to Azure Storage successfully");

            progress?.UpdateProgress(85, "Finalizing package upload...");
            await CommitFileAsync(appId, contentVersionId, fileId, intuneWinInfo.EncryptionInfo, ct);
            uploadLogger.Success("File committed successfully");

            progress?.UpdateProgress(90, "Processing uploaded package...");
            await WaitForFileProcessingAsync(appId, contentVersionId, fileId, "CommitFile", ct);
            uploadLogger.Success("File processing completed");

            // Switching the committed version is the only PATCH: it carries just
            // committedContentVersion, so every other property keeps its tenant value.
            progress?.UpdateProgress(95, "Switching the app to the new content...");
            await CommitAppAsync(appId, contentVersionId, ct);
            uploadLogger.Success($"App now serves content version {contentVersionId}");

            progress?.UpdateProgress(100, "Update complete!");
            uploadLogger.Section("UPDATE COMPLETE");
            uploadLogger.Success($"Package content for '{appName}' updated. App ID: {appId}");

            PackageMarker.SaveMarker(packagePath, appId, appName, version);

            return contentVersionId;
        }
        catch (OperationCanceledException)
        {
            // The old content version stays committed, so a cancelled update is a no-op
            // for devices; the orphaned version is harmless and ages out with the app.
            uploadLogger.Section("UPDATE CANCELLED");
            throw;
        }
        catch (Exception ex)
        {
            uploadLogger.Section("UPDATE FAILED");
            uploadLogger.Error("Update process failed", ex);
            throw new Exception($"Failed to update the package in Intune: {ex.Message}", ex);
        }
        finally
        {
            if (intuneWinInfo != null) CleanupTempFiles(intuneWinInfo);
        }
    }

    /// <summary>
    /// The converter names the output after the setup file, so a package built under an
    /// older name can leave a stale sibling; the freshest file is the one just written.
    /// </summary>
    private static string? FindNewestIntuneWinFile(string packagePath)
    {
        var intuneFolder = Path.Combine(packagePath, "Intune");
        if (!Directory.Exists(intuneFolder)) return null;

        return Directory.GetFiles(intuneFolder, "*.intunewin")
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .FirstOrDefault();
    }
}
