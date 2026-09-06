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
    /// Probes read access to each service. Successful reads do not establish write
    /// permissions or the administrator roles required for publishing and group changes.
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
            await ProbeAsync("Intune apps · read access", $"{Base}?$top=1&$select=id", ct),
            await ProbeAsync("Entra groups · read access", $"{GraphClient.Groups}?$top=1&$select=id", ct),
            await ProbeAsync("Entra devices · read access", $"{GraphClient.Devices}?$top=1&$select=id", ct),
            await ProbeAsync("Entra users · read access", $"{GraphClient.Users}?$top=1&$select=id", ct),
        };

        var ok = checks.All(c => c.Ok);
        return new ConnectionTestResult
        {
            Success = ok,
            Message = ok
                ? "Read checks passed. Publishing and group changes also require write permissions and applicable administrator roles; these checks do not verify them."
                : "One or more Microsoft Graph read checks failed. Check the results below; write access has not been verified.",
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
                ? "Read access denied"
                : $"HTTP {response.StatusCode}";
            return new ConnectionCheck { Name = name, Ok = false, Detail = detail };
        }
        catch (Exception ex)
        {
            return new ConnectionCheck { Name = name, Ok = false, Detail = ex.Message };
        }
    }
}
