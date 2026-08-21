using System.Net;
using System.Net.Http;

namespace Packman.Services;

public sealed class ConnectionCheck
{
    public string Name { get; init; } = "";
    public bool Ok { get; init; }
    public string Detail { get; init; } = "";
}

public sealed class ConnectionTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<ConnectionCheck> Checks { get; init; } = Array.Empty<ConnectionCheck>();
}

public partial class IntuneService
{
    /// <summary>Probes one endpoint per required scope to confirm sign-in and consent.</summary>
    public async Task<ConnectionTestResult> TestConnectionAsync()
    {
        try
        {
            await _tokenProvider();
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult
            {
                Success = false,
                Message = $"Could not acquire a Microsoft Graph token: {ex.Message}",
            };
        }

        var checks = new List<ConnectionCheck>
        {
            await ProbeAsync("Intune apps · DeviceManagementApps.ReadWrite.All", $"{Base}?$top=1"),
            await ProbeAsync("Entra groups · Group.Read.All", "https://graph.microsoft.com/beta/groups?$top=1&$select=id"),
            await ProbeAsync("Entra devices · Device.Read.All", "https://graph.microsoft.com/beta/devices?$top=1&$select=id"),
        };

        var ok = checks.All(c => c.Ok);
        return new ConnectionTestResult
        {
            Success = ok,
            Message = ok
                ? "Connected to Microsoft Intune. All required scopes are available."
                : "Connected to Microsoft Graph, but one or more required scopes are missing or not consented.",
            Checks = checks,
        };
    }

    private async Task<ConnectionCheck> ProbeAsync(string name, string url)
    {
        try
        {
            using var request = await AuthRequestAsync(HttpMethod.Get, url);
            var response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return new ConnectionCheck { Name = name, Ok = true, Detail = "OK" };

            var detail = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                ? "Access denied — scope not consented"
                : $"HTTP {(int)response.StatusCode}";
            return new ConnectionCheck { Name = name, Ok = false, Detail = detail };
        }
        catch (Exception ex)
        {
            return new ConnectionCheck { Name = name, Ok = false, Detail = ex.Message };
        }
    }
}
