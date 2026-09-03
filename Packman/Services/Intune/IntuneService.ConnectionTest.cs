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
    /// <summary>
    /// Probes one read endpoint per scope family to confirm sign-in and consent. The
    /// probes only read, so they prove the ReadWrite scopes were consented rather than
    /// exercising the writes themselves.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await _graph.GetTokenAsync();
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
            await ProbeAsync("Intune apps · DeviceManagementApps.ReadWrite.All", $"{Base}?$top=1&$select=id", ct),
            await ProbeAsync("Entra groups · Group.ReadWrite.All", $"{GraphClient.Groups}?$top=1&$select=id", ct),
            await ProbeAsync("Entra devices · Device.Read.All", $"{GraphClient.Devices}?$top=1&$select=id", ct),
            await ProbeAsync("Entra users · User.ReadBasic.All", $"{GraphClient.Users}?$top=1&$select=id", ct),
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

    private async Task<ConnectionCheck> ProbeAsync(string name, string url, CancellationToken ct)
    {
        try
        {
            var response = await _graph.GetAsync(url, name, ct, throwOnError: false);
            if (response.IsSuccess)
                return new ConnectionCheck { Name = name, Ok = true, Detail = "OK" };

            var detail = response.StatusCode is 401 or 403
                ? "Access denied — scope not consented"
                : $"HTTP {response.StatusCode}";
            return new ConnectionCheck { Name = name, Ok = false, Detail = detail };
        }
        catch (Exception ex)
        {
            return new ConnectionCheck { Name = name, Ok = false, Detail = ex.Message };
        }
    }
}
