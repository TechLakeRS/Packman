namespace Packman.Services;

/// <summary>
/// App-wide singletons. No DI container; the sign-in done on the Settings page has to
/// be reachable from the upload flow.
/// </summary>
public static class AppServices
{
    public static SettingsService Settings { get; } = new();
    public static IntuneAuthService Auth { get; } = new();
    public static IntuneService Apps { get; } = new(() => Auth.GetAccessTokenAsync());
}
