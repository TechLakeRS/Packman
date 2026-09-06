namespace Packman.Services;

/// <summary>
/// Signs one file in place. Implementations own certificate/key access and must report
/// failure when the requested signature or timestamp cannot be produced.
/// </summary>
public interface IFileSigner
{
    Task<SigningResult> SignFileAsync(string filePath, CancellationToken cancellationToken = default);
}
