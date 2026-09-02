using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Packman.Models;
using System.Security.Cryptography.X509Certificates;

namespace Packman.Services;

public class IntuneAuthService
{
    // "Microsoft Graph Command Line Tools", the client Connect-MgGraph uses. Present in
    // virtually every tenant, so it works when no Client ID is configured.
    private const string DefaultInteractiveClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e";

    private static readonly string[] InteractiveScopes =
    [
        "User.Read",
        "DeviceManagementApps.ReadWrite.All",
        // Group search plus "create a group per package" (POST /groups needs ReadWrite).
        "Group.ReadWrite.All",
        // Advanced screen: PC lookup, user lookup and group membership.
        "Device.Read.All",
        "User.ReadBasic.All",
        "GroupMember.ReadWrite.All",
    ];

    private static readonly string[] AppOnlyScopes = ["https://graph.microsoft.com/.default"];

    private IPublicClientApplication? _pca;
    private IConfidentialClientApplication? _cca;
    private IAccount? _account;

    public string? SignedInUser { get; private set; }

    /// <summary>Raised on sign-in state changes so screens can refresh.</summary>
    public event Action? StateChanged;

    public async Task SignInAsync(AuthMode mode, AppSettings.AuthConfig cfg, nint hwnd)
    {
        if (mode == AuthMode.AppRegistration && !string.IsNullOrWhiteSpace(cfg.CertificateThumbprint))
            await SignInWithCertificateAsync(cfg);
        else
            await SignInInteractiveAsync(cfg, hwnd);

        StateChanged?.Invoke();
    }

    private async Task SignInInteractiveAsync(AppSettings.AuthConfig cfg, nint hwnd)
    {
        var clientId = string.IsNullOrWhiteSpace(cfg.ClientId)
            ? DefaultInteractiveClientId
            : cfg.ClientId.Trim();

        var authority = string.IsNullOrWhiteSpace(cfg.TenantId)
            ? "https://login.microsoftonline.com/organizations"
            : $"https://login.microsoftonline.com/{cfg.TenantId.Trim()}";

        _pca = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(authority)
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
            .Build();

        AuthenticationResult result;
        try
        {
            var accounts = await _pca.GetAccountsAsync();
            result = await _pca.AcquireTokenSilent(InteractiveScopes, accounts.FirstOrDefault()).ExecuteAsync();
        }
        catch (MsalUiRequiredException)
        {
            result = await _pca.AcquireTokenInteractive(InteractiveScopes)
                .WithParentActivityOrWindow(hwnd)
                .ExecuteAsync();
        }

        _cca = null;
        _account = result.Account;
        SignedInUser = result.Account.Username;
    }

    private async Task SignInWithCertificateAsync(AppSettings.AuthConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.ClientId))
            throw new InvalidOperationException("App registration mode requires a Client ID.");
        if (string.IsNullOrWhiteSpace(cfg.TenantId))
            throw new InvalidOperationException("App registration mode requires a Tenant ID.");

        var certificate = FindCertificate(cfg.CertificateThumbprint);
        var authority = $"https://login.microsoftonline.com/{cfg.TenantId.Trim()}";

        _cca = ConfidentialClientApplicationBuilder
            .Create(cfg.ClientId.Trim())
            .WithAuthority(authority)
            .WithCertificate(certificate)
            .Build();

        // Acquire once so a bad cert or missing consent fails here, not at upload time.
        await _cca.AcquireTokenForClient(AppOnlyScopes).ExecuteAsync();

        _pca = null;
        _account = null;
        SignedInUser = $"App registration {cfg.ClientId.Trim()}";
    }

    private static X509Certificate2 FindCertificate(string thumbprint)
    {
        var clean = thumbprint.Replace(" ", "").Trim();
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, clean, validOnly: false);
            if (found.Count > 0)
                return found[0];
        }

        throw new InvalidOperationException(
            $"Certificate with thumbprint '{thumbprint}' was not found in the CurrentUser or LocalMachine store.");
    }

    public async Task SignOutAsync()
    {
        if (_pca != null && _account != null)
            await _pca.RemoveAsync(_account);
        _account = null;
        _cca = null;
        _pca = null;
        SignedInUser = null;
        StateChanged?.Invoke();
    }

    public bool IsSignedIn => _cca != null || (_pca != null && _account != null);

    /// <summary>Graph access token for the current sign-in. Throws when not signed in.</summary>
    public async Task<string> GetAccessTokenAsync()
    {
        if (_cca != null)
        {
            var appResult = await _cca.AcquireTokenForClient(AppOnlyScopes).ExecuteAsync();
            return appResult.AccessToken;
        }

        if (_pca == null || _account == null)
            throw new InvalidOperationException("Not signed in. Sign in on the Settings page before uploading.");

        try
        {
            var result = await _pca.AcquireTokenSilent(InteractiveScopes, _account).ExecuteAsync();
            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            // Refresh token revoked, password changed, or a Conditional Access policy kicked
            // in. The session is over; say so instead of surfacing a raw MSAL error while the
            // footer still reads "Connected".
            _account = null;
            SignedInUser = null;
            StateChanged?.Invoke();
            throw new InvalidOperationException(
                "The Intune sign-in has expired. Sign in again on the Settings page.", ex);
        }
    }
}
