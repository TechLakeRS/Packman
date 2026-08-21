using Packman.Helpers;
using Packman.Models;
using System.Net.Http;
using System.Text.Json;

namespace Packman.Services;

public partial class IntuneService
{
    /// <summary>Searches security groups by name prefix. Needs Group.Read.All.</summary>
    public async Task<List<EntraGroup>> SearchGroupsAsync(string query)
    {
        var results = new List<EntraGroup>();
        if (string.IsNullOrWhiteSpace(query))
            return results;

        var filter = Uri.EscapeDataString($"startswith(displayName,'{OData.Literal(query.Trim())}')");
        var url = $"https://graph.microsoft.com/beta/groups?$filter={filter}&$select=id,displayName&$top=20&$orderby=displayName";

        using var request = await AuthRequestAsync(HttpMethod.Get, url);
        var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Group search failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var g in arr.EnumerateArray())
                results.Add(new EntraGroup
                {
                    Id = g.GetSafeString("id"),
                    DisplayName = g.TryGetProperty("displayName", out var n) ? n.GetString() ?? "" : "",
                });

        return results;
    }
}
