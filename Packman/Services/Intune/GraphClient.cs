using Packman.Helpers;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Packman.Services;

/// <summary>A Graph call that came back non-2xx. Carries status and body so callers can branch on them.</summary>
public sealed class GraphException : Exception
{
    public string Operation { get; }
    public int StatusCode { get; }
    public string Body { get; }
    public string? RequestId { get; }

    public GraphException(string operation, int statusCode, string body, string? requestId)
        : base($"{operation} failed (HTTP {statusCode}): {ExtractMessage(body)}")
    {
        Operation = operation;
        StatusCode = statusCode;
        Body = body;
        RequestId = requestId;
    }

    public bool IsForbidden => StatusCode is 401 or 403;
    public bool IsNotFound => StatusCode == 404;
    public bool IsServerError => StatusCode >= 500;

    /// <summary>Graph error bodies are {"error":{"code","message"}}; the message reads better than the JSON.</summary>
    public static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "no response body";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.GetSafeString("message");
                var code = error.GetSafeString("code");
                if (!string.IsNullOrEmpty(message))
                    return string.IsNullOrEmpty(code) ? message : $"{code}: {message}";
            }
        }
        catch (JsonException) { }
        return body.Length > 500 ? body[..500] + "…" : body;
    }
}

/// <summary>Status and body of one Graph call.</summary>
public sealed class GraphResponse
{
    public int StatusCode { get; }
    public bool IsSuccess { get; }
    public string Body { get; }
    public string? RequestId { get; }

    internal GraphResponse(int statusCode, bool isSuccess, string body, string? requestId)
    {
        StatusCode = statusCode;
        IsSuccess = isSuccess;
        Body = body;
        RequestId = requestId;
    }

    /// <summary>The parsed body; an undefined element when there is none.</summary>
    public JsonElement Json => string.IsNullOrWhiteSpace(Body) ? default : JsonSerializer.Deserialize<JsonElement>(Body);

    public GraphException ToException(string operation) => new(operation, StatusCode, Body, RequestId);
}

/// <summary>
/// The one HTTP path to Microsoft Graph: bearer token per call, JSON in and out,
/// Retry-After honoured on 429/503/504, transient network failures retried, and every
/// failure surfaced as a <see cref="GraphException"/> with the status and Graph's message.
/// </summary>
public sealed class GraphClient
{
    public const string Beta = "https://graph.microsoft.com/beta";
    public const string MobileApps = Beta + "/deviceAppManagement/mobileApps";
    public const string Groups = Beta + "/groups";
    public const string Devices = Beta + "/devices";
    public const string Users = Beta + "/users";

    private const int MaxAttempts = 5;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(2);

    // One client per process: a new handler per call exhausts sockets on a long upload.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly JsonSerializerOptions BodyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Func<Task<string>> _tokenProvider;

    public GraphClient(Func<Task<string>> tokenProvider)
        => _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

    /// <summary>Acquires a token without calling anything, to fail early on a dead sign-in.</summary>
    public Task<string> GetTokenAsync() => _tokenProvider();

    public Task<GraphResponse> GetAsync(string url, string operation, CancellationToken ct = default, bool throwOnError = true)
        => SendAsync(HttpMethod.Get, url, null, operation, ct, throwOnError);

    public Task<GraphResponse> PostAsync(string url, object? body, string operation, CancellationToken ct = default, bool throwOnError = true)
        => SendAsync(HttpMethod.Post, url, body ?? "{}", operation, ct, throwOnError);

    public Task<GraphResponse> PatchAsync(string url, object body, string operation, CancellationToken ct = default, bool throwOnError = true)
        => SendAsync(HttpMethod.Patch, url, body, operation, ct, throwOnError);

    public Task<GraphResponse> DeleteAsync(string url, string operation, CancellationToken ct = default, bool throwOnError = true)
        => SendAsync(HttpMethod.Delete, url, null, operation, ct, throwOnError);

    /// <summary>
    /// Sends one request. <paramref name="body"/> is serialised as JSON, or sent verbatim
    /// when it is already a string.
    /// </summary>
    public async Task<GraphResponse> SendAsync(HttpMethod method, string url, object? body, string operation,
        CancellationToken ct = default, bool throwOnError = true)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var token = await _tokenProvider();
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null)
            {
                var json = body as string ?? JsonSerializer.Serialize(body, BodyOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await Http.SendAsync(request, ct);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(Backoff(attempt), ct);
                continue;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < MaxAttempts)
            {
                // HttpClient timeout, not the caller's token.
                await Task.Delay(Backoff(attempt), ct);
                continue;
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                var status = (int)response.StatusCode;

                if (status is 429 or 503 or 504 && attempt < MaxAttempts)
                {
                    await Task.Delay(RetryAfter(response) ?? Backoff(attempt), ct);
                    continue;
                }

                var result = new GraphResponse(status, response.IsSuccessStatusCode, text, Header(response, "request-id"));
                if (!result.IsSuccess && throwOnError)
                    throw result.ToException(operation);
                return result;
            }
        }
    }

    /// <summary>Walks a collection through @odata.nextLink, yielding each element of "value".</summary>
    public async IAsyncEnumerable<JsonElement> GetAllPagesAsync(string url, string operation,
        [EnumeratorCancellation] CancellationToken ct = default, IProgress<int>? progress = null)
    {
        var count = 0;
        while (!string.IsNullOrEmpty(url))
        {
            var page = (await GetAsync(url, operation, ct)).Json;
            if (page.ValueKind == JsonValueKind.Object && page.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    count++;
                    yield return item;
                }
            }
            progress?.Report(count);
            url = page.ValueKind == JsonValueKind.Object ? page.GetSafeString("@odata.nextLink") : "";
        }
    }

    private static TimeSpan Backoff(int attempt)
        => TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        TimeSpan? delay = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (delay is null || delay <= TimeSpan.Zero) return null;
        return delay > MaxRetryAfter ? MaxRetryAfter : delay;
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
