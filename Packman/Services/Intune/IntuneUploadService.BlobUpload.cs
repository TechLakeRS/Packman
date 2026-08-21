using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Packman.Services;

public partial class IntuneUploadService
{
    // Azure caps a block blob at 50,000 blocks and wants every id the same length, so the
    // index is padded to a width that covers the cap.
    private const int MaxBlocks = 50_000;
    private const string BlockIdFormat = "00000";

    private async Task<string> CreateContentVersionAsync(string appId, CancellationToken ct)
    {
        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions";
        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await sharedHttpClient!.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to create content version. Status: {response.StatusCode}, Response: {responseText}");

        var contentVersion = JsonSerializer.Deserialize<JsonElement>(responseText);
        var contentVersionId = contentVersion.GetProperty("id").GetString();
        return contentVersionId ?? throw new Exception("Content version ID not returned");
    }

    private async Task<string> CreateFileEntryAsync(string appId, string contentVersionId, IntuneWinInfo intuneWinInfo, CancellationToken ct)
    {
        var encryptedSize = new FileInfo(intuneWinInfo.EncryptedFilePath).Length;

        var fileBody = new Dictionary<string, object?>
        {
            ["@odata.type"] = "#microsoft.graph.mobileAppContentFile",
            ["name"] = intuneWinInfo.FileName,
            ["size"] = intuneWinInfo.UnencryptedContentSize,
            ["sizeEncrypted"] = encryptedSize,
            ["manifest"] = null,
            ["isDependency"] = false
        };

        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files";
        var json = JsonSerializer.Serialize(fileBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await sharedHttpClient!.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to create file entry. Status: {response.StatusCode}, Response: {responseText}");

        var fileEntry = JsonSerializer.Deserialize<JsonElement>(responseText);
        var fileId = fileEntry.GetProperty("id").GetString();
        return fileId ?? throw new Exception("File ID not returned");
    }

    private async Task<AzureStorageInfo> WaitForAzureStorageUriAsync(string appId, string contentVersionId, string fileId, CancellationToken ct)
    {
        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}";

        for (int attempts = 0; attempts < 120; attempts++) // 20 minutes total
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url);
                var response = await sharedHttpClient!.SendAsync(request, ct);
                var responseText = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Failed to get file info. Status: {response.StatusCode}, Response: {responseText}");

                var fileInfo = JsonSerializer.Deserialize<JsonElement>(responseText);

                if (!fileInfo.TryGetProperty("uploadState", out var uploadStateProp))
                {
                    await Task.Delay(10000, ct);
                    continue;
                }

                var uploadState = uploadStateProp.GetString() ?? "";

                if (uploadState.Equals("AzureStorageUriRequestSuccess", StringComparison.OrdinalIgnoreCase))
                {
                    if (fileInfo.TryGetProperty("azureStorageUri", out var azureStorageUriProp))
                    {
                        var azureStorageUri = azureStorageUriProp.GetString();
                        return new AzureStorageInfo { SasUri = azureStorageUri ?? throw new Exception("Azure Storage URI is null") };
                    }
                    throw new Exception("Upload state is success but azureStorageUri is missing");
                }

                if (uploadState.Equals("AzureStorageUriRequestPending", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(10000, ct);
                    continue;
                }

                if (uploadState.Equals("AzureStorageUriRequestFailed", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Azure Storage URI request failed");

                if (uploadState.Equals("AzureStorageUriRequestTimedOut", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Azure Storage URI request timed out");

                if (attempts < 115)
                {
                    await Task.Delay(15000, ct);
                    continue;
                }

                throw new Exception($"Unknown upload state after many attempts: '{uploadState}'. Check Intune admin center for app status.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (!(ex.Message.Contains("upload state") || ex.Message.Contains("Failed to get file info")))
            {
                Debug.WriteLine($"Network exception on attempt {attempts + 1}: {ex.Message}");
                if (attempts < 115)
                {
                    await Task.Delay(10000, ct);
                    continue;
                }
                throw;
            }
        }

        throw new Exception("Timeout waiting for Azure Storage URI after 20 minutes. The application was created in Intune but file upload preparation timed out.");
    }

    private async Task UploadFileToAzureStorageAsync(string sasUri, string filePath, IUploadProgress? progress, CancellationToken ct)
    {
        var fileInfo = new FileInfo(filePath);
        var totalSize = fileInfo.Length;

        int chunkSize = totalSize > 5L * 1024 * 1024 * 1024 ? 4 * 1024 * 1024 : 6 * 1024 * 1024;
        var totalChunks = (int)Math.Ceiling((double)totalSize / chunkSize);

        if (totalChunks > MaxBlocks)
            throw new Exception($"Package is too large to upload: it would need {totalChunks:N0} blocks and Azure allows {MaxBlocks:N0}.");

        progress?.UpdateProgress(65, $"Preparing upload ({FormatBytes(totalSize)})...");

        using var azureHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var blockIds = new List<string>(totalChunks);
        var sasRenewalTimer = Stopwatch.StartNew();
        var currentSasUri = sasUri;

        // One buffer for the run: a fresh 6 MB array per chunk goes straight to the LOH.
        var buffer = new byte[chunkSize];

        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(chunkIndex.ToString(BlockIdFormat)));
            blockIds.Add(blockId);

            long startPosition = (long)chunkIndex * chunkSize;
            int bytesToRead = (int)Math.Min(chunkSize, totalSize - startPosition);

            fileStream.Position = startPosition;
            var totalBytesRead = 0;
            while (totalBytesRead < bytesToRead)
            {
                var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(totalBytesRead, bytesToRead - totalBytesRead), ct);
                if (bytesRead == 0)
                    break;
                totalBytesRead += bytesRead;
            }

            var percentComplete = (int)((long)chunkIndex * 100 / totalChunks);
            var progressPercentage = 65 + (int)((chunkIndex + 1.0) / totalChunks * 15);
            progress?.UpdateProgress(progressPercentage, $"Uploading chunk {chunkIndex + 1}/{totalChunks} ({percentComplete}%)");

            if (chunkIndex < totalChunks - 1 && sasRenewalTimer.ElapsedMilliseconds >= 420000)
            {
                progress?.UpdateProgress(progressPercentage, "Renewing SAS token...");
                try
                {
                    currentSasUri = await RenewSasUriAsync(ct);
                    sasRenewalTimer.Restart();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SAS renewal failed, continuing with current token: {ex.Message}");
                }
            }

            var chunkUri = $"{currentSasUri}&comp=block&blockid={Uri.EscapeDataString(blockId)}";
            await UploadChunkWithRetryAsync(azureHttpClient, chunkUri, buffer, totalBytesRead, chunkIndex, totalChunks, ct);
        }

        progress?.UpdateProgress(82, "Finalizing Azure upload...");
        await CommitBlockListWithRetryAsync(azureHttpClient, currentSasUri, blockIds, ct);
        progress?.UpdateProgress(84, "Package uploaded to Azure");
    }

    private async Task UploadChunkWithRetryAsync(
        HttpClient client, string chunkUri, byte[] buffer, int length, int chunkIndex, int totalChunks, CancellationToken ct)
    {
        const int maxRetries = 5;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, chunkUri);
                request.Content = new ByteArrayContent(buffer, 0, length);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "iso-8859-1" };
                request.Headers.Add("x-ms-blob-type", "BlockBlob");

                var timeout = chunkIndex == totalChunks - 1 ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5 + attempt);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                var response = await client.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                    return;

                var errorText = await response.Content.ReadAsStringAsync(ct);
                Debug.WriteLine($"Chunk {chunkIndex + 1} failed (attempt {attempt}/{maxRetries}): {response.StatusCode} {errorText}");

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500
                    && response.StatusCode != System.Net.HttpStatusCode.RequestTimeout
                    && (int)response.StatusCode != 429)
                {
                    throw new Exception($"Client error: {response.StatusCode} - {errorText}");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"Chunk {chunkIndex + 1} timed out (attempt {attempt}/{maxRetries})");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Network error on chunk {chunkIndex + 1} (attempt {attempt}/{maxRetries}): {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error on chunk {chunkIndex + 1} (attempt {attempt}/{maxRetries}): {ex.Message}");
                if (attempt == maxRetries)
                    throw;
            }

            if (attempt < maxRetries)
            {
                var baseDelay = Math.Pow(2, attempt);
                var jitter = Random.Shared.NextDouble();
                await Task.Delay(TimeSpan.FromSeconds(baseDelay + baseDelay * jitter), ct);
            }
            else
            {
                throw new Exception($"Failed to upload chunk {chunkIndex + 1} after {maxRetries} attempts");
            }
        }
    }

    private async Task CommitBlockListWithRetryAsync(HttpClient client, string sasUri, List<string> blockIds, CancellationToken ct)
    {
        var blockListXml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>");
        foreach (var blockId in blockIds)
            blockListXml.Append("<Latest>").Append(blockId).Append("</Latest>");
        blockListXml.Append("</BlockList>");
        var body = blockListXml.ToString();

        const int maxRetries = 5;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var finalizeUri = $"{sasUri}&comp=blocklist";
                using var request = new HttpRequestMessage(HttpMethod.Put, finalizeUri);
                request.Content = new StringContent(body, Encoding.UTF8);
                request.Content.Headers.ContentType = null;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(10));
                var response = await client.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                    return;

                var errorText = await response.Content.ReadAsStringAsync(ct);
                Debug.WriteLine($"Block list commit failed (attempt {attempt}/{maxRetries}): {response.StatusCode} {errorText}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"Block list commit timed out (attempt {attempt}/{maxRetries})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error committing block list (attempt {attempt}/{maxRetries}): {ex.Message}");
            }

            if (attempt < maxRetries)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2), ct);
            else
                throw new Exception($"Failed to commit block list after {maxRetries} attempts");
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F2} GB";
        if (bytes >= 1048576) return $"{bytes / 1048576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} bytes";
    }

    private async Task CommitFileAsync(string appId, string contentVersionId, string fileId, EncryptionInfo encryptionInfo, CancellationToken ct)
    {
        var commitBody = new Dictionary<string, object>
        {
            ["fileEncryptionInfo"] = new Dictionary<string, object>
            {
                ["encryptionKey"] = encryptionInfo.EncryptionKey ?? "",
                ["macKey"] = encryptionInfo.MacKey ?? "",
                ["initializationVector"] = encryptionInfo.InitializationVector ?? "",
                ["mac"] = encryptionInfo.Mac ?? "",
                ["profileIdentifier"] = encryptionInfo.ProfileIdentifier ?? "ProfileVersion1",
                ["fileDigest"] = encryptionInfo.FileDigest ?? "",
                ["fileDigestAlgorithm"] = encryptionInfo.FileDigestAlgorithm ?? "SHA256"
            }
        };

        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}/commit";
        var json = JsonSerializer.Serialize(commitBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await sharedHttpClient!.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to commit file. Status: {response.StatusCode}, Response: {responseText}");
    }

    private async Task WaitForFileProcessingAsync(string appId, string contentVersionId, string fileId, string stage, CancellationToken ct)
    {
        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}";
        var successState = $"{stage}Success";
        var pendingState = $"{stage}Pending";

        for (int attempts = 0; attempts < 120; attempts++)
        {
            ct.ThrowIfCancellationRequested();

            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url);
            var response = await sharedHttpClient!.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to get file processing status. Status: {response.StatusCode}, Response: {responseText}");

            var fileInfo = JsonSerializer.Deserialize<JsonElement>(responseText);

            if (!fileInfo.TryGetProperty("uploadState", out var uploadStateProp))
            {
                await Task.Delay(5000, ct);
                continue;
            }

            var uploadState = uploadStateProp.GetString() ?? "";

            if (uploadState.Equals(successState, StringComparison.OrdinalIgnoreCase))
                return;

            if (uploadState.Equals(pendingState, StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(5000, ct);
                continue;
            }

            if (uploadState.Equals($"{stage}Failed", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"File processing failed for stage: {stage}. State: {uploadState}");

            if (uploadState.Equals($"{stage}TimedOut", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"File processing timed out for stage: {stage}. State: {uploadState}");

            if (attempts < 115)
            {
                await Task.Delay(10000, ct);
                continue;
            }

            throw new Exception($"Unknown file processing state after many attempts: '{uploadState}'. Check Intune admin center.");
        }

        throw new Exception($"Timeout waiting for file processing stage: {stage} after 10 minutes");
    }

    private async Task CommitAppAsync(string appId, string contentVersionId, CancellationToken ct)
    {
        var commitBody = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobApp",
            ["committedContentVersion"] = contentVersionId
        };

        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}";
        var json = JsonSerializer.Serialize(commitBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var request = await CreateAuthenticatedRequestAsync(new HttpMethod("PATCH"), url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var sw = Stopwatch.StartNew();
        var response = await sharedHttpClient!.SendAsync(request, ct);
        sw.Stop();

        if (response.IsSuccessStatusCode)
            return;

        var responseText = await response.Content.ReadAsStringAsync(ct);
        LogGraphFailureDiagnostics("CommitApp (PATCH)", request, response, sw, responseText);

        // A gateway 5xx often means the backend finished but exceeded the sync timeout;
        // read the committed version back before calling it a failure.
        if ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            var actual = await TryGetCommittedVersionAsync(appId, ct);
            if (actual.GetCommitted == contentVersionId)
                return;
        }

        throw new Exception($"Failed to commit app. Status: {response.StatusCode}, Response: {responseText}");
    }

    private readonly record struct VerifyResult(string? GetCommitted, bool VerifyFailed, string? FailureReason);

    private async Task<VerifyResult> TryGetCommittedVersionAsync(string appId, CancellationToken ct)
    {
        try
        {
            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}";
            using var req = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url);
            var resp = await sharedHttpClient!.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return new VerifyResult(null, true, $"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("committedContentVersion", out var v))
                return new VerifyResult(v.ValueKind == JsonValueKind.String ? v.GetString() : null, false, null);
            return new VerifyResult(null, false, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new VerifyResult(null, true, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LogGraphFailureDiagnostics(
        string operation, HttpRequestMessage request, HttpResponseMessage response, Stopwatch stopwatch, string responseBody)
    {
        Debug.WriteLine($"=== GRAPH FAILURE: {operation} ===");
        Debug.WriteLine($"  Method/URL: {request.Method} {request.RequestUri}");
        Debug.WriteLine($"  Status: {(int)response.StatusCode} {response.StatusCode}");
        Debug.WriteLine($"  Duration: {stopwatch.Elapsed.TotalSeconds:F1}s");

        foreach (var name in new[] { "request-id", "client-request-id", "x-ms-ags-diagnostic", "Retry-After", "Date" })
        {
            if (response.Headers.TryGetValues(name, out var values))
                Debug.WriteLine($"  {name}: {string.Join(", ", values)}");
        }

        Debug.WriteLine($"  Body: {responseBody}");
    }

    private static void CleanupTempFiles(IntuneWinInfo intuneWinInfo)
    {
        try
        {
            if (Directory.Exists(intuneWinInfo.TempDirectory))
                Directory.Delete(intuneWinInfo.TempDirectory, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to cleanup temp files: {ex.Message}");
        }
    }

    private async Task<string> RenewSasUriAsync(CancellationToken ct)
    {
        var renewUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{_currentAppId}/microsoft.graph.win32LobApp/contentVersions/{_currentContentVersionId}/files/{_currentFileId}/renewUpload";

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, renewUrl);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await sharedHttpClient!.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"Failed to renew SAS URI. Status: {response.StatusCode}, Response: {responseText}");
        }

        return await WaitForNewSasUriAfterRenewalAsync(ct);
    }

    private async Task<string> WaitForNewSasUriAfterRenewalAsync(CancellationToken ct)
    {
        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{_currentAppId}/microsoft.graph.win32LobApp/contentVersions/{_currentContentVersionId}/files/{_currentFileId}";

        for (int attempts = 0; attempts < 30; attempts++)
        {
            ct.ThrowIfCancellationRequested();

            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url);
            var response = await sharedHttpClient!.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to get renewed SAS URI. Status: {response.StatusCode}, Response: {responseText}");

            var fileInfo = JsonSerializer.Deserialize<JsonElement>(responseText);

            if (fileInfo.TryGetProperty("uploadState", out var uploadStateProp) &&
                (uploadStateProp.GetString() ?? "").Equals("AzureStorageUriRenewalSuccess", StringComparison.OrdinalIgnoreCase) &&
                fileInfo.TryGetProperty("azureStorageUri", out var azureStorageUriProp))
            {
                var newSasUri = azureStorageUriProp.GetString();
                if (!string.IsNullOrEmpty(newSasUri))
                    return newSasUri;
            }

            await Task.Delay(10000, ct);
        }

        throw new Exception("Timeout waiting for SAS URI renewal");
    }
}
