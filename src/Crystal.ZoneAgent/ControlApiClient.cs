using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crystal.Agent.Contracts;

namespace Crystal.ZoneAgent;

internal sealed record RemoteRelease(
    long Id,
    string Version,
    string Status,
    string ContentHash,
    int SchemaVersion,
    int FormatVersion);

internal sealed class ControlApiException : Exception
{
    public ControlApiException(HttpStatusCode statusCode, string detail)
        : base($"Control API returned {(int)statusCode} {statusCode}: {detail}")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

internal sealed class ControlApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _zoneId;

    public ControlApiClient(AgentOptions options)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(options.ControlApiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(15)
        };
        _zoneId = options.ZoneId;
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
        }
    }

    public async Task<ZoneDto> RegisterAsync(
        AgentRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            AgentApiRoutes.Register.TrimStart('/'),
            request,
            AgentContractJson.Options,
            cancellationToken);
        return await ReadRequiredAsync<ZoneDto>(response, cancellationToken);
    }

    public async Task HeartbeatAsync(
        AgentHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            $"api/v1/zones/{Uri.EscapeDataString(_zoneId)}/heartbeat",
            request,
            AgentContractJson.Options,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentCommand>> ClaimCommandsAsync(
        int limit,
        int leaseSeconds,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            $"api/v1/zones/{Uri.EscapeDataString(_zoneId)}/commands" +
            $"?claim=true&limit={limit}&leaseSeconds={leaseSeconds}",
            cancellationToken);
        var payload = await ReadRequiredAsync<AgentCommandListResponse>(response, cancellationToken);
        return payload.Items;
    }

    public async Task<ZoneCommandDto> CompleteCommandAsync(
        AgentCommandResult result,
        CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            $"api/v1/zones/{Uri.EscapeDataString(_zoneId)}/commands/" +
            $"{Uri.EscapeDataString(result.CommandId)}/result",
            result,
            AgentContractJson.Options,
            cancellationToken);
        return await ReadRequiredAsync<ZoneCommandDto>(response, cancellationToken);
    }

    public async Task<RemoteRelease> GetReleaseAsync(
        long releaseId,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            $"api/v1/releases/{releaseId}",
            cancellationToken);
        var json = await ReadRequiredAsync<JsonElement>(response, cancellationToken);
        return new RemoteRelease(
            json.GetProperty("id").GetInt64(),
            json.GetProperty("version").GetString() ?? string.Empty,
            json.GetProperty("status").GetString() ?? string.Empty,
            json.GetProperty("contentHash").GetString() ?? string.Empty,
            json.GetProperty("schemaVersion").GetInt32(),
            json.GetProperty("formatVersion").GetInt32());
    }

    public async Task DownloadArtifactAsync(
        long releaseId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            $"api/v1/releases/{releaseId}/artifact",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var value = await response.Content.ReadFromJsonAsync<T>(
            AgentContractJson.Options,
            cancellationToken);
        return value ?? throw new InvalidDataException("Control API returned an empty JSON response.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string detail = await response.Content.ReadAsStringAsync(cancellationToken);
        if (detail.Length > 2000) detail = detail[..2000];
        throw new ControlApiException(response.StatusCode, detail);
    }
}
