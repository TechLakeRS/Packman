namespace Packman.Services;

/// <summary>Confirmed or uncertain publication must retain the app while follow-up work is reviewed.</summary>
public sealed class IntuneFollowUpException(string appId, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string AppId { get; } = appId;
}
