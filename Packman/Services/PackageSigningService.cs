using System.IO;
using Packman.Helpers;

namespace Packman.Services;

/// <summary>Shared signing stage for desktop publishing and future integration hosts.</summary>
public static class PackageSigningService
{
    /// <returns>The signed script path, or null when signing was explicitly disabled.</returns>
    public static async Task<string?> SignAsync(string packagePath, IFileSigner? signer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (signer is null) return null;

        var scriptPath = PsadtScript.Find(packagePath)
            ?? throw new FileNotFoundException("The deployment script could not be found for signing.");
        var result = await signer.SignFileAsync(scriptPath, ct);
        ct.ThrowIfCancellationRequested();
        if (!result.Success)
        {
            var reason = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "The signing provider reported a failure." : result.ErrorMessage;
            throw new InvalidOperationException($"Code signing failed. Publishing stopped: {reason}");
        }

        return scriptPath;
    }
}
