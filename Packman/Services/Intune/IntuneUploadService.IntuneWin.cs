using Packman.Helpers;
using Packman.Models;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Packman.Services;

public partial class IntuneUploadService
{
    private async Task SignApplicationFilesAsync(string packagePath, IUploadProgress? progress, UploadLogger uploadLogger, CancellationToken ct)
    {
        try
        {
            uploadLogger.Section("FILE SIGNING");

            // No signer configured, or no usable certificate.
            if (_signer == null || !_signer.IsCertificateAvailable())
            {
                progress?.UpdateProgress(15, "Code signing disabled - skipping file signing");
                uploadLogger.Warning("Code signing disabled or certificate not available - skipping file signing");
                return;
            }

            var scriptPath = FindPSADTScript(packagePath);
            if (scriptPath == null)
            {
                uploadLogger.Warning("No PSADT script found - skipping signing");
                return;
            }

            var scriptName = Path.GetFileName(scriptPath);
            progress?.UpdateProgress(10, $"Signing {scriptName}...");
            uploadLogger.Info($"Signing {scriptName} (SHA-256 + RFC 3161)");

            var result = await _signer.SignFileAsync(scriptPath, ct);
            if (result.Success)
            {
                progress?.UpdateProgress(15, $"{scriptName} signed successfully");
                uploadLogger.Success($"{scriptName} signed successfully");
            }
            else
            {
                uploadLogger.Warning($"Failed to sign {scriptName}: {result.ErrorMessage}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            uploadLogger.Error("File signing failed", ex);
            progress?.UpdateProgress(15, "File signing failed - continuing with upload");
        }
    }

    private static string? FindPSADTScript(string packagePath)
    {
        var path = Path.Combine(packagePath, "Application", PsadtLayout.ScriptName);
        if (File.Exists(path)) return path;

        path = Path.Combine(packagePath, PsadtLayout.ScriptName);
        return File.Exists(path) ? path : null;
    }

    private async Task CreateIntuneWinFileAsync(string packagePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_converterPath) || !File.Exists(_converterPath))
            throw new FileNotFoundException($"IntuneWinAppUtil.exe not found at: '{_converterPath}'. Set the IntuneWinAppUtil path on the Settings page.");

        var applicationFolder = Path.Combine(packagePath, "Application");
        var setupFile = Path.Combine(applicationFolder, PsadtLayout.SetupFileName);
        var outputFolder = Path.Combine(packagePath, "Intune");

        if (!Directory.Exists(applicationFolder))
            throw new DirectoryNotFoundException($"Application folder not found: {applicationFolder}");

        if (!File.Exists(setupFile))
            throw new FileNotFoundException(
                $"{PsadtLayout.SetupFileName} not found in: {applicationFolder}. Packman builds PSADT v4 packages; check the PSADT Template Path on the Settings page.");

        Directory.CreateDirectory(outputFolder);

        var arguments = $"-c \"{applicationFolder}\" -s \"{setupFile}\" -o \"{outputFolder}\" -q";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = _converterPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        // Drain both pipes concurrently: in sequence this deadlocks as soon as the child
        // fills the buffer of the stream we are not reading yet.
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var errorMessage = string.IsNullOrWhiteSpace(errorTask.Result) ? outputTask.Result : errorTask.Result;
            throw new Exception($"IntuneWinAppUtil failed with exit code {process.ExitCode}: {errorMessage}");
        }

        Debug.WriteLine($"Created .intunewin file using: {_converterPath} {arguments}");
    }

    private IntuneWinInfo ExtractIntuneWinInfo(string intuneWinFilePath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            using var archive = ZipFile.OpenRead(intuneWinFilePath);

            var detectionEntry = archive.Entries.FirstOrDefault(e =>
                e.Name.Equals("detection.xml", StringComparison.OrdinalIgnoreCase));

            if (detectionEntry == null)
            {
                var availableFiles = string.Join(", ", archive.Entries.Select(e => e.Name));
                throw new Exception($"detection.xml not found. Available files: {availableFiles}");
            }

            var detectionXmlPath = Path.Combine(tempDir, "detection.xml");
            detectionEntry.ExtractToFile(detectionXmlPath);

            var detectionXml = XDocument.Load(detectionXmlPath);
            var appInfo = detectionXml.Root;

            if (appInfo == null || !appInfo.Name.LocalName.Equals("ApplicationInfo", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Root element is not ApplicationInfo. Found: {appInfo?.Name}");

            var encryptionInfo = appInfo.Elements().FirstOrDefault(e => e.Name.LocalName == "EncryptionInfo");
            if (encryptionInfo == null)
            {
                var availableElements = string.Join(", ", appInfo.Elements().Select(e => e.Name.LocalName));
                throw new Exception($"EncryptionInfo not found. Available elements: {availableElements}");
            }

            ZipArchiveEntry? contentEntry = null;

            var commonDatNames = new[] { "Contents.dat", "IntunePackage.dat", "contents.dat", "intunepackage.dat" };
            foreach (var datName in commonDatNames)
            {
                contentEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals(datName, StringComparison.OrdinalIgnoreCase));
                if (contentEntry != null)
                    break;
            }

            contentEntry ??= archive.Entries.FirstOrDefault(e =>
                e.Name.EndsWith(".intunewin", StringComparison.OrdinalIgnoreCase) && e.Length > 1000);

            contentEntry ??= archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));

            contentEntry ??= archive.Entries
                .Where(e => !e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Length)
                .FirstOrDefault();

            if (contentEntry == null)
            {
                var fileList = string.Join("\n  ", archive.Entries.Select(e => $"{e.Name} ({e.Length:N0} bytes)"));
                throw new Exception($"Could not find encrypted content file in archive.\n\nAvailable files:\n  {fileList}");
            }

            var encryptedFilePath = Path.Combine(tempDir, contentEntry.Name);
            contentEntry.ExtractToFile(encryptedFilePath);

            string GetElementValue(XElement parent, string localName)
            {
                var element = parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
                return element?.Value?.Trim() ?? "";
            }

            var fileName = GetElementValue(appInfo, "FileName");
            if (string.IsNullOrEmpty(fileName))
                fileName = Path.GetFileName(intuneWinFilePath);

            var unencryptedSizeStr = GetElementValue(appInfo, "UnencryptedContentSize");
            if (!long.TryParse(unencryptedSizeStr, out var unencryptedSize))
                unencryptedSize = contentEntry.Length;

            var result = new IntuneWinInfo
            {
                FileName = fileName,
                UnencryptedContentSize = unencryptedSize,
                EncryptedFilePath = encryptedFilePath,
                TempDirectory = tempDir,
                EncryptionInfo = new EncryptionInfo
                {
                    EncryptionKey = GetElementValue(encryptionInfo, "EncryptionKey"),
                    MacKey = GetElementValue(encryptionInfo, "MacKey"),
                    InitializationVector = GetElementValue(encryptionInfo, "InitializationVector"),
                    Mac = GetElementValue(encryptionInfo, "Mac"),
                    ProfileIdentifier = GetElementValue(encryptionInfo, "ProfileIdentifier"),
                    FileDigest = GetElementValue(encryptionInfo, "FileDigest"),
                    FileDigestAlgorithm = GetElementValue(encryptionInfo, "FileDigestAlgorithm")
                }
            };

            if (string.IsNullOrEmpty(result.EncryptionInfo.ProfileIdentifier))
                result.EncryptionInfo.ProfileIdentifier = "ProfileVersion1";
            if (string.IsNullOrEmpty(result.EncryptionInfo.FileDigestAlgorithm))
                result.EncryptionInfo.FileDigestAlgorithm = "SHA256";

            if (string.IsNullOrEmpty(result.EncryptionInfo.EncryptionKey))
                throw new Exception("EncryptionKey is missing from detection.xml");

            if (!File.Exists(result.EncryptedFilePath))
                throw new Exception($"Encrypted content file was not extracted properly: {result.EncryptedFilePath}");

            return result;
        }
        catch
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            throw;
        }
    }

    private async Task<string> CreateWin32LobAppAsync(
        ApplicationInfo appInfo,
        string installCommand,
        string uninstallCommand,
        string description,
        List<DetectionRule> detectionRules,
        string installContext,
        IntuneWinInfo intuneWinInfo,
        string? iconPath = null,
        RequirementInfo? requirements = null,
        List<ReturnCodeInfo>? returnCodes = null,
        string? privacyUrl = null,
        string? informationUrl = null,
        CancellationToken ct = default)
    {
        var formattedDetectionRules = new List<Dictionary<string, object>>();
        foreach (var rule in detectionRules)
        {
            var formattedRule = ConvertDetectionRuleForBetaAPI(rule);
            if (formattedRule != null)
                formattedDetectionRules.Add(formattedRule);
        }

        // A guessed rule uploads fine and then never detects the app, so refuse instead.
        if (formattedDetectionRules.Count == 0)
            throw new InvalidOperationException(
                "No usable detection rule was supplied. Set a detection rule on the Upload step before publishing.");

        var createAppPayload = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobApp",
            ["displayName"] = appInfo.DisplayName,
            ["description"] = description,
            ["publisher"] = appInfo.Manufacturer,
            ["displayVersion"] = appInfo.Version,
            ["installCommandLine"] = installCommand,
            ["uninstallCommandLine"] = uninstallCommand,
            ["applicableArchitectures"] = "x86,x64,arm64",
            ["minimumSupportedOperatingSystem"] = new Dictionary<string, object>
            {
                [(requirements ?? new RequirementInfo()).OperatingSystemFlag] = true
            },
            ["fileName"] = intuneWinInfo.FileName,
            ["setupFilePath"] = PsadtLayout.SetupFileName,
            ["installExperience"] = new Dictionary<string, object>
            {
                ["runAsAccount"] = installContext,
                ["deviceRestartBehavior"] = "allow"
            },
            ["detectionRules"] = formattedDetectionRules.ToArray(),
            ["returnCodes"] = (returnCodes is { Count: > 0 } ? returnCodes : ReturnCodeInfo.Defaults())
                .Select(rc => new Dictionary<string, object> { ["returnCode"] = rc.Code, ["type"] = rc.GraphType })
                .ToArray()
        };

        if (!string.IsNullOrWhiteSpace(privacyUrl))
            createAppPayload["privacyInformationUrl"] = privacyUrl.Trim();
        if (!string.IsNullOrWhiteSpace(informationUrl))
            createAppPayload["informationUrl"] = informationUrl.Trim();

        if (requirements?.MinimumFreeDiskSpaceMB is int disk)
            createAppPayload["minimumFreeDiskSpaceInMB"] = disk;
        if (requirements?.MinimumMemoryMB is int mem)
            createAppPayload["minimumMemoryInMB"] = mem;
        if (requirements?.MinimumNumberOfProcessors is int cpus)
            createAppPayload["minimumNumberOfProcessors"] = cpus;
        if (requirements?.MinimumCpuSpeedMHz is int mhz)
            createAppPayload["minimumCpuSpeedInMHz"] = mhz;

        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            var iconData = ConvertIconToBase64(iconPath);
            if (iconData != null)
                createAppPayload["largeIcon"] = iconData;
        }

        var json = JsonSerializer.Serialize(createAppPayload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "https://graph.microsoft.com/beta/deviceAppManagement/mobileApps");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var sw = Stopwatch.StartNew();
        var response = await sharedHttpClient!.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            LogGraphFailureDiagnostics("CreateWin32LobApp (POST)", request, response, sw, responseText);
            throw new Exception($"Failed to create Win32 app. Status: {response.StatusCode}, Response: {responseText}");
        }

        var createdApp = JsonSerializer.Deserialize<JsonElement>(responseText);
        var appId = createdApp.GetProperty("id").GetString();

        return appId ?? throw new Exception("App ID not returned from creation");
    }

    private Dictionary<string, object>? ConvertIconToBase64(string iconPath)
    {
        try
        {
            var iconBytes = File.ReadAllBytes(iconPath);
            var base64String = Convert.ToBase64String(iconBytes);
            var extension = Path.GetExtension(iconPath).ToLower();
            var mimeType = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".ico" => "image/x-icon",
                _ => "image/png"
            };

            return new Dictionary<string, object>
            {
                ["type"] = mimeType,
                ["value"] = base64String
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error converting icon to base64: {ex.Message}");
            return null;
        }
    }
}
