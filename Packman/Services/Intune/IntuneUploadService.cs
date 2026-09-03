using Packman.Helpers;
using Packman.Models;
using System.IO;

namespace Packman.Services;

public interface IUploadProgress
{
    void UpdateProgress(int percentage, string message);
}

/// <summary>Intune reported a terminal upload state. Never retried.</summary>
public sealed class UploadStateException : Exception
{
    public string State { get; }
    public UploadStateException(string stage, string state)
        : base($"Intune reported '{state}' while waiting for {stage}. Check the app in the Intune admin center.")
        => State = state;
}

/// <summary>
/// Uploads a Win32 (PSADT) package to Intune via Graph: sign, build the .intunewin,
/// register the app, push the encrypted payload to Azure Storage, commit, publish, assign.
/// </summary>
public partial class IntuneUploadService
{
    private readonly GraphClient _graph;
    private readonly NativeCodeSigner? _signer;
    private readonly string _converterPath;

    /// <summary>Whether the half-built app was removed after a failure or cancel. Null when nothing had been created.</summary>
    public bool? RollbackSucceeded { get; private set; }

    public IntuneUploadService(Func<Task<string>> tokenProvider, NativeCodeSigner? signer, string converterPath)
    {
        _graph = new GraphClient(tokenProvider);
        _signer = signer;
        _converterPath = converterPath;
    }

    public async Task<string> UploadWin32ApplicationAsync(
        ApplicationInfo appInfo,
        string packagePath,
        List<DetectionRule> detectionRules,
        string installCommand,
        string uninstallCommand,
        string description,
        string installContext,
        string? iconPath = null,
        IUploadProgress? progress = null,
        string? predecessorAppId = null,
        AppSettings.GroupAssignmentConfig? groupAssignment = null,
        RequirementInfo? requirements = null,
        List<ReturnCodeInfo>? returnCodes = null,
        string? privacyUrl = null,
        string? informationUrl = null,
        IEnumerable<AssignedGroup>? pickedGroups = null,
        CancellationToken ct = default)
    {
        using var log = new UploadLogger(appInfo.Name);
        string? createdAppId = null;
        RollbackSucceeded = null;

        void Report(int pct, string message)
        {
            progress?.UpdateProgress(pct, message);
            log.Progress(pct, message);
        }

        try
        {
            log.Section("UPLOAD METADATA");
            log.LogMetadata("Application Name", appInfo.Name);
            log.LogMetadata("Manufacturer", appInfo.Manufacturer);
            log.LogMetadata("Version", appInfo.Version);
            log.LogMetadata("Package Path", packagePath);
            log.LogMetadata("Install Command", installCommand);
            log.LogMetadata("Uninstall Command", uninstallCommand);
            log.LogMetadata("Install Context", installContext);
            log.LogMetadata("Detection Rules", $"{detectionRules.Count} rule(s)");
            log.Info($"Upload log file: {log.LogFilePath}");

            log.Section("UPLOAD PROCESS");

            Report(5, "Authenticating with Microsoft Graph...");
            _ = await _graph.GetTokenAsync();
            log.Success("Authentication successful");

            Report(10, "Signing application files...");
            await SignApplicationFilesAsync(packagePath, progress, log, ct);

            Report(20, "Packaging application files...");
            var intuneWinFile = await CreateIntuneWinFileAsync(packagePath, ct);
            log.Success($"Package created: {Path.GetFileName(intuneWinFile)}");

            Report(30, "Reading package metadata...");
            var intuneWin = ExtractIntuneWinInfo(intuneWinFile);
            log.Success($"Package metadata extracted - Size: {intuneWin.UnencryptedContentSize:N0} bytes");

            ct.ThrowIfCancellationRequested();

            Report(35, "Registering application in Intune...");
            var appId = await CreateWin32LobAppAsync(appInfo, installCommand, uninstallCommand, description, detectionRules,
                installContext, intuneWin, iconPath, requirements, returnCodes, privacyUrl, informationUrl, ct);
            createdAppId = appId;
            log.Success($"Application registered with ID: {appId}");

            Report(45, "Preparing content storage...");
            var contentVersionId = await CreateContentVersionAsync(appId, ct);
            log.Success($"Content version created: {contentVersionId}");

            Report(50, "Initializing file upload...");
            var fileId = await CreateFileEntryAsync(appId, contentVersionId, intuneWin, ct);
            var fileUrl = FileUrl(appId, contentVersionId, fileId);
            log.Success($"File entry created: {fileId}");

            Report(55, "Requesting Azure upload URL...");
            var storage = await WaitForUploadStateAsync(fileUrl, "azureStorageUriRequest", TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(10), ct);
            var sasUri = storage.GetSafeString("azureStorageUri");
            if (string.IsNullOrEmpty(sasUri))
                throw new UploadStateException("the Azure Storage URI", "azureStorageUriRequestSuccess without a URI");
            log.Success("Azure Storage URI obtained");

            await UploadToAzureStorageAsync(sasUri, fileUrl, intuneWin, progress, log, ct);
            log.Success("Package uploaded to Azure Storage successfully");

            Report(85, "Finalizing package upload...");
            await CommitFileAsync(fileUrl, intuneWin.EncryptionInfo, ct);
            log.Success("File committed successfully");

            Report(90, "Processing uploaded package...");
            await WaitForUploadStateAsync(fileUrl, "commitFile", TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5), ct);
            log.Success("File processing completed");

            Report(95, "Publishing application...");
            await CommitAppAsync(appId, contentVersionId, ct);
            log.Success("Application published successfully");

            // Published: a later supersedence or assignment failure must not roll it back.
            createdAppId = null;

            PackageMarker.SaveMarker(packagePath, appId, appInfo.Name, appInfo.Version);

            if (!string.IsNullOrEmpty(predecessorAppId))
                await WriteSupersedenceAsync(appId, predecessorAppId, log, ct);

            var picked = pickedGroups?.Where(g => !string.IsNullOrWhiteSpace(g.GroupId)).ToList() ?? new List<AssignedGroup>();
            if (picked.Count > 0 || (groupAssignment?.HasAnyAssignment() ?? false))
            {
                Report(98, "Assigning groups...");
                log.Section("GROUP ASSIGNMENT");
                await AssignGroupsAsync(appId, appInfo, groupAssignment ?? new AppSettings.GroupAssignmentConfig(), picked, log, ct);
            }

            Report(100, "Upload complete!");
            log.Section("UPLOAD COMPLETE");
            log.Success($"Application '{appInfo.Name}' uploaded successfully! ID: {appId}");
            return appId;
        }
        catch (OperationCanceledException)
        {
            log.Section("UPLOAD CANCELLED");
            await RollbackCreatedAppAsync(createdAppId, log);
            throw;
        }
        catch (Exception ex)
        {
            log.Section("UPLOAD FAILED");
            log.Error("Upload process failed", ex);
            await RollbackCreatedAppAsync(createdAppId, log);
            throw;
        }
    }

    /// <summary>
    /// Drops the half-built app so a failed upload leaves no shell in the tenant. Best
    /// effort, and not cancellable: it runs after a cancel.
    /// </summary>
    private async Task RollbackCreatedAppAsync(string? appId, UploadLogger log)
    {
        if (string.IsNullOrEmpty(appId)) return;

        try
        {
            var response = await _graph.DeleteAsync($"{GraphClient.MobileApps}/{appId}", "Remove incomplete app", CancellationToken.None, throwOnError: false);
            RollbackSucceeded = response.IsSuccess;
            if (response.IsSuccess)
                log.Info($"Removed the incomplete app {appId} from Intune");
            else
                log.Warning($"Could not remove the incomplete app {appId} (HTTP {response.StatusCode}) - delete it in the Intune admin center");
        }
        catch (Exception ex)
        {
            RollbackSucceeded = false;
            log.Warning($"Could not remove the incomplete app {appId}: {ex.Message}");
        }
    }

    private async Task WriteSupersedenceAsync(string newAppId, string predecessorAppId, UploadLogger log, CancellationToken ct)
    {
        var payload = new
        {
            relationships = new object[]
            {
                new Dictionary<string, string>
                {
                    ["@odata.type"] = "#microsoft.graph.mobileAppSupersedence",
                    ["targetId"] = predecessorAppId,
                    ["targetType"] = "child",
                    ["supersedenceType"] = "update",
                },
            },
        };

        try
        {
            var response = await _graph.PostAsync($"{GraphClient.MobileApps}/{newAppId}/updateRelationships", payload, "Write supersedence", ct, throwOnError: false);
            if (response.IsSuccess)
                log.Success($"Marked previous app {predecessorAppId} as superseded");
            else
                log.Warning($"Could not write supersedence (HTTP {response.StatusCode}): {GraphException.ExtractMessage(response.Body)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.Warning($"Could not write supersedence: {ex.Message}");
        }
    }
}

/// <summary>What the upload needs to know about a built .intunewin.</summary>
public sealed class IntuneWinInfo
{
    public string IntuneWinPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public long UnencryptedContentSize { get; init; }
    /// <summary>Name of the encrypted payload entry inside the archive.</summary>
    public string ContentEntryName { get; init; } = "";
    public long EncryptedContentSize { get; init; }
    public EncryptionInfo EncryptionInfo { get; init; } = new();
}

public sealed class EncryptionInfo
{
    public string EncryptionKey { get; init; } = "";
    public string MacKey { get; init; } = "";
    public string InitializationVector { get; init; } = "";
    public string Mac { get; init; } = "";
    public string ProfileIdentifier { get; init; } = "ProfileVersion1";
    public string FileDigest { get; init; } = "";
    public string FileDigestAlgorithm { get; init; } = "SHA256";
}
