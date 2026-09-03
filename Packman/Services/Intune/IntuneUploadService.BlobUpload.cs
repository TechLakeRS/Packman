using Packman.Helpers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
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
    private const int MaxBlockAttempts = 5;

    // SAS URIs from Intune are good for about 15 minutes; renew well inside that.
    private static readonly TimeSpan SasRenewalInterval = TimeSpan.FromMinutes(7);

    // Per-request timeouts come from a linked token; the client itself never times out.
    private static readonly HttpClient AzureBlob = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static string FileUrl(string appId, string contentVersionId, string fileId)
        => $"{GraphClient.MobileApps}/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}";

    private async Task<string> CreateContentVersionAsync(string appId, CancellationToken ct)
    {
        var url = $"{GraphClient.MobileApps}/{appId}/microsoft.graph.win32LobApp/contentVersions";
        var created = (await _graph.PostAsync(url, "{}", "Create content version", ct)).Json;
        return created.GetSafeString("id") is { Length: > 0 } id ? id : throw new Exception("Content version ID not returned");
    }

    private async Task<string> CreateFileEntryAsync(string appId, string contentVersionId, IntuneWinInfo intuneWin, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["@odata.type"] = "#microsoft.graph.mobileAppContentFile",
            ["name"] = intuneWin.FileName,
            ["size"] = intuneWin.UnencryptedContentSize,
            ["sizeEncrypted"] = intuneWin.EncryptedContentSize,
            ["manifest"] = null,
            ["isDependency"] = false,
        };
        var url = $"{GraphClient.MobileApps}/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files";
        var created = (await _graph.PostAsync(url, body, "Create file entry", ct)).Json;
        return created.GetSafeString("id") is { Length: > 0 } id ? id : throw new Exception("File ID not returned");
    }

    /// <summary>
    /// Polls the content file until its uploadState reaches "{stage}Success". "{stage}Pending"
    /// and an absent state keep waiting; Failed and TimedOut are terminal and throw
    /// <see cref="UploadStateException"/>; Graph's own 429/5xx are retried by the client.
    /// </summary>
    private async Task<JsonElement> WaitForUploadStateAsync(string fileUrl, string stage, TimeSpan timeout, TimeSpan interval, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        var success = stage + "Success";
        var pending = stage + "Pending";

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var file = (await _graph.GetAsync(fileUrl, $"Read upload state ({stage})", ct)).Json;
            var state = file.GetSafeString("uploadState");

            if (state.Equals(success, StringComparison.OrdinalIgnoreCase))
                return file;

            if (state.EndsWith("Failed", StringComparison.OrdinalIgnoreCase) ||
                state.EndsWith("TimedOut", StringComparison.OrdinalIgnoreCase))
                throw new UploadStateException(stage, state);

            if (!string.IsNullOrEmpty(state) && !state.Equals(pending, StringComparison.OrdinalIgnoreCase))
                Debug.WriteLine($"Unexpected upload state '{state}' while waiting for {success}; still waiting.");

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Timed out after {timeout.TotalMinutes:0} minutes waiting for {success} (last state: '{state}'). The app exists in Intune without content.");

            await Task.Delay(interval, ct);
        }
    }

    /// <summary>
    /// Streams the encrypted payload out of the .intunewin straight into block PUTs, then
    /// commits the block list. Reports progress in the 60-84 band.
    /// </summary>
    private async Task UploadToAzureStorageAsync(string sasUri, string fileUrl, IntuneWinInfo intuneWin,
        IUploadProgress? progress, UploadLogger log, CancellationToken ct)
    {
        var totalSize = intuneWin.EncryptedContentSize;
        int chunkSize = totalSize > 5L * 1024 * 1024 * 1024 ? 4 * 1024 * 1024 : 6 * 1024 * 1024;
        var totalChunks = (int)Math.Ceiling((double)totalSize / chunkSize);

        if (totalChunks > MaxBlocks)
            throw new Exception($"Package is too large to upload: it would need {totalChunks:N0} blocks and Azure allows {MaxBlocks:N0}.");

        progress?.UpdateProgress(60, $"Uploading package to Azure ({ByteSize.Format(totalSize)})...");

        using var archive = ZipFile.OpenRead(intuneWin.IntuneWinPath);
        var entry = archive.GetEntry(intuneWin.ContentEntryName)
            ?? throw new FileNotFoundException($"Payload entry '{intuneWin.ContentEntryName}' is missing from the .intunewin.");
        using var payload = entry.Open();

        var blockIds = new List<string>(totalChunks);
        var sasTimer = Stopwatch.StartNew();
        var currentSasUri = sasUri;

        // One buffer for the run: a fresh 6 MB array per chunk goes straight to the LOH.
        var buffer = new byte[chunkSize];

        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(chunkIndex.ToString(BlockIdFormat)));
            blockIds.Add(blockId);

            int bytesToRead = (int)Math.Min(chunkSize, totalSize - (long)chunkIndex * chunkSize);
            int read = 0;
            while (read < bytesToRead)
            {
                var n = await payload.ReadAsync(buffer.AsMemory(read, bytesToRead - read), ct);
                if (n == 0) throw new EndOfStreamException($"Payload ended after {(long)chunkIndex * chunkSize + read:N0} of {totalSize:N0} bytes.");
                read += n;
            }

            var percent = (int)((long)chunkIndex * 100 / totalChunks);
            var band = 60 + (int)((chunkIndex + 1.0) / totalChunks * 22);
            progress?.UpdateProgress(band, $"Uploading chunk {chunkIndex + 1}/{totalChunks} ({percent}%)");

            if (chunkIndex < totalChunks - 1 && sasTimer.Elapsed >= SasRenewalInterval)
            {
                progress?.UpdateProgress(band, "Renewing upload URL...");
                currentSasUri = await RenewSasUriAsync(fileUrl, log, ct);
                sasTimer.Restart();
            }

            var chunkUri = $"{currentSasUri}&comp=block&blockid={Uri.EscapeDataString(blockId)}";
            await PutBlockAsync(chunkUri, buffer, read, chunkIndex, totalChunks, ct);
        }

        progress?.UpdateProgress(83, "Finalizing Azure upload...");
        await PutBlockListAsync(currentSasUri, blockIds, ct);
        progress?.UpdateProgress(84, "Package uploaded to Azure");
    }

    private static async Task PutBlockAsync(string chunkUri, byte[] buffer, int length, int chunkIndex, int totalChunks, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            HttpStatusCode? status = null;
            string failure;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, chunkUri);
                request.Content = new ByteArrayContent(buffer, 0, length);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                request.Headers.Add("x-ms-blob-type", "BlockBlob");

                var timeout = chunkIndex == totalChunks - 1 ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5 + attempt);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);

                using var response = await AzureBlob.SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode) return;

                status = response.StatusCode;
                failure = $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { failure = "timed out"; }
            catch (HttpRequestException ex) { failure = ex.Message; }

            Debug.WriteLine($"Block {chunkIndex + 1}/{totalChunks} attempt {attempt}/{MaxBlockAttempts} failed: {failure}");

            // A 4xx other than 408/429 will not get better on retry: an expired SAS, a bad block.
            if (status is { } s && (int)s is >= 400 and < 500 && s != HttpStatusCode.RequestTimeout && (int)s != 429)
                throw new Exception($"Azure Storage rejected block {chunkIndex + 1}: {failure}");

            if (attempt >= MaxBlockAttempts)
                throw new Exception($"Failed to upload block {chunkIndex + 1} after {MaxBlockAttempts} attempts: {failure}");

            var baseDelay = Math.Pow(2, attempt);
            await Task.Delay(TimeSpan.FromSeconds(baseDelay + baseDelay * Random.Shared.NextDouble()), ct);
        }
    }

    private static async Task PutBlockListAsync(string sasUri, List<string> blockIds, CancellationToken ct)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>");
        foreach (var blockId in blockIds)
            xml.Append("<Latest>").Append(blockId).Append("</Latest>");
        xml.Append("</BlockList>");
        var body = xml.ToString();

        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            string failure;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, $"{sasUri}&comp=blocklist");
                request.Content = new StringContent(body, Encoding.UTF8);
                request.Content.Headers.ContentType = null;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(10));

                using var response = await AzureBlob.SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode) return;
                failure = $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { failure = "timed out"; }
            catch (HttpRequestException ex) { failure = ex.Message; }

            Debug.WriteLine($"Block list commit attempt {attempt}/{MaxBlockAttempts} failed: {failure}");
            if (attempt >= MaxBlockAttempts)
                throw new Exception($"Failed to commit block list after {MaxBlockAttempts} attempts: {failure}");

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2), ct);
        }
    }

    /// <summary>Asks Intune for a fresh SAS URI. One retry; after that the upload cannot continue anyway.</summary>
    private async Task<string> RenewSasUriAsync(string fileUrl, UploadLogger log, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await _graph.PostAsync($"{fileUrl}/renewUpload", "{}", "Renew upload URL", ct);
                var file = await WaitForUploadStateAsync(fileUrl, "azureStorageUriRenewal", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10), ct);
                var renewed = file.GetSafeString("azureStorageUri");
                if (string.IsNullOrEmpty(renewed))
                    throw new UploadStateException("the renewed Azure Storage URI", "azureStorageUriRenewalSuccess without a URI");
                log.Info("Upload URL renewed");
                return renewed;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt == 1)
            {
                log.Warning($"Upload URL renewal failed, retrying once: {ex.Message}");
            }
        }
    }

    private async Task CommitFileAsync(string fileUrl, EncryptionInfo encryption, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["fileEncryptionInfo"] = new Dictionary<string, object>
            {
                ["encryptionKey"] = encryption.EncryptionKey,
                ["macKey"] = encryption.MacKey,
                ["initializationVector"] = encryption.InitializationVector,
                ["mac"] = encryption.Mac,
                ["profileIdentifier"] = encryption.ProfileIdentifier,
                ["fileDigest"] = encryption.FileDigest,
                ["fileDigestAlgorithm"] = encryption.FileDigestAlgorithm,
            },
        };
        await _graph.PostAsync($"{fileUrl}/commit", body, "Commit file", ct);
    }

    private async Task CommitAppAsync(string appId, string contentVersionId, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobApp",
            ["committedContentVersion"] = contentVersionId,
        };

        var url = $"{GraphClient.MobileApps}/{appId}";
        var response = await _graph.PatchAsync(url, body, "Publish app", ct, throwOnError: false);
        if (response.IsSuccess) return;

        // A gateway 5xx often means the backend finished but exceeded the sync timeout;
        // read the committed version back before calling it a failure.
        if (response.StatusCode >= 500)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            var app = await _graph.GetAsync(url, "Verify publish", ct, throwOnError: false);
            if (app.IsSuccess && app.Json.GetSafeString("committedContentVersion") == contentVersionId)
                return;
        }

        throw response.ToException("Publish app");
    }
}
