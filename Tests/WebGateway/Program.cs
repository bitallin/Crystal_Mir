using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Crystal.Web.Gateway;
using Microsoft.AspNetCore.Builder;

const string Origin = "https://web.test";
Environment.SetEnvironmentVariable("Logging__LogLevel__Default", "Warning");
Environment.SetEnvironmentVariable("Logging__LogLevel__Microsoft.AspNetCore", "Warning");

await BootstrapAndHealth();
ProductionRequiresBuildIdentity();
await MissingOriginIsRejected();
await InvalidOriginIsRejected();
await SubprotocolIsRequired();
await BytesAreBridgedInBothDirections();
await OversizedFramesAreClosed();
await RateLimitedClientsAreClosed();
await IdleConnectionsAreClosed();
await UnavailableUpstreamIsReported();

Console.WriteLine("Web gateway checks passed.");
return 0;

async Task BootstrapAndHealth()
{
    await using GatewayFixture fixture = await GatewayFixture.StartAsync(options => options);
    using HttpClient client = new();
    using JsonDocument bootstrap = JsonDocument.Parse(await client.GetStringAsync($"{fixture.BaseUrl}/api/web/bootstrap"));
    Require(bootstrap.RootElement.GetProperty("protocol").GetString() == GatewayOptions.Protocol, "Bootstrap protocol mismatch.");
    Require(bootstrap.RootElement.GetProperty("zoneId").GetString() == "test", "Bootstrap zone mismatch.");
    Require((await client.GetStringAsync($"{fixture.BaseUrl}/health/live")).Contains("live", StringComparison.Ordinal), "Live health failed.");
    using JsonDocument metrics = JsonDocument.Parse(await client.GetStringAsync($"{fixture.BaseUrl}/metrics"));
    Require(metrics.RootElement.TryGetProperty("rateLimited", out _), "Metrics missing rate limiter counter.");
}

void ProductionRequiresBuildIdentity()
{
    string? previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    string? previousHash = Environment.GetEnvironmentVariable("CRYSTAL_WEB_CLIENT_BUILD_HASH");
    string? previousVersion = Environment.GetEnvironmentVariable("CRYSTAL_WEB_ASSET_VERSION");
    try
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("CRYSTAL_WEB_CLIENT_BUILD_HASH", "sha256:dev");
        Environment.SetEnvironmentVariable("CRYSTAL_WEB_ASSET_VERSION", "dev");
        bool rejected = false;
        try
        {
            GatewayApp.Build([]);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Require(rejected, "Production gateway accepted development build identity.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnvironment);
        Environment.SetEnvironmentVariable("CRYSTAL_WEB_CLIENT_BUILD_HASH", previousHash);
        Environment.SetEnvironmentVariable("CRYSTAL_WEB_ASSET_VERSION", previousVersion);
    }
}

async Task MissingOriginIsRejected()
{
    await using GatewayFixture fixture = await GatewayFixture.StartAsync(options => options);
    using ClientWebSocket socket = new();
    socket.Options.AddSubProtocol(GatewayOptions.Protocol);
    await ExpectConnectFailure(socket, fixture.WebSocketUri(), "Missing Origin was accepted.");
}

async Task InvalidOriginIsRejected()
{
    await using GatewayFixture fixture = await GatewayFixture.StartAsync(options => options);
    using ClientWebSocket socket = new();
    socket.Options.AddSubProtocol(GatewayOptions.Protocol);
    socket.Options.SetRequestHeader("Origin", "https://evil.test");
    await ExpectConnectFailure(socket, fixture.WebSocketUri(), "Disallowed Origin was accepted.");
}

async Task SubprotocolIsRequired()
{
    await using GatewayFixture fixture = await GatewayFixture.StartAsync(options => options);
    using ClientWebSocket socket = new();
    socket.Options.SetRequestHeader("Origin", Origin);
    await ExpectConnectFailure(socket, fixture.WebSocketUri(), "WebSocket without crystal-v1 was accepted.");
}

async Task BytesAreBridgedInBothDirections()
{
    await using GatewayFixture fixture = await GatewayFixture.StartAsync(options => options);
    using ClientWebSocket socket = await fixture.ConnectAsync();
    Require(socket.SubProtocol == GatewayOptions.Protocol, "Negotiated subprotocol mismatch.");
    byte[] payload = [0x02, 0x00, 0x00, 0x00, 0x10, 0x20, 0x30];
    await socket.SendAsync(payload, WebSocketMessageType.Binary, true, CancellationToken.None);
    byte[] echoed = await ReceiveBinaryAsync(socket, TimeSpan.FromSeconds(3));
    Require(payload.SequenceEqual(echoed), "Gateway did not preserve bytes in both directions.");
}

async Task OversizedFramesAreClosed()
{
    await using GatewayFixture fixture = await GatewayFixture.StartAsync(options => new GatewayOptions
    {
        ZoneId = options.ZoneId,
        ZoneTcp = options.ZoneTcp,
        AllowedOrigins = options.AllowedOrigins,
        MaxFrameBytes = 32,
        IdleTimeoutSeconds = 10,
    });
    using ClientWebSocket socket = await fixture.ConnectAsync();
    await socket.SendAsync(new byte[33], WebSocketMessageType.Binary, true, CancellationToken.None);
    WebSocketCloseStatus status = await ReceiveUntilCloseAsync(socket, TimeSpan.FromSeconds(3));
    Require(status == WebSocketCloseStatus.MessageTooBig, $"Expected 1009, got {status}.");
}

async Task RateLimitedClientsAreClosed()
{
    await using GatewayFixture fixture = await GatewayFixture.StartAsync(options => new GatewayOptions
    {
        ZoneId = options.ZoneId,
        ZoneTcp = options.ZoneTcp,
        AllowedOrigins = options.AllowedOrigins,
        MaxFramesPerSecond = 2,
        MaxBytesPerSecond = 1024,
        IdleTimeoutSeconds = 10,
    });
    using ClientWebSocket socket = await fixture.ConnectAsync();
    byte[] payload = [1, 2, 3, 4];
    for (int index = 0; index < 6; index++)
    {
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        catch (WebSocketException)
        {
            break;
        }
    }
    WebSocketCloseStatus status = await ReceiveUntilCloseAsync(socket, TimeSpan.FromSeconds(3));
    Require(status == WebSocketCloseStatus.PolicyViolation, $"Expected 1008 for rate limit, got {status}.");
}

async Task IdleConnectionsAreClosed()
{
    await using GatewayFixture fixture = await GatewayFixture.StartAsync(options => new GatewayOptions
    {
        ZoneId = options.ZoneId,
        ZoneTcp = options.ZoneTcp,
        AllowedOrigins = options.AllowedOrigins,
        IdleTimeoutSeconds = 1,
    });
    using ClientWebSocket socket = await fixture.ConnectAsync();
    WebSocketCloseStatus status = await ReceiveUntilCloseAsync(socket, TimeSpan.FromSeconds(4));
    Require(status == WebSocketCloseStatus.PolicyViolation, $"Expected idle 1008, got {status}.");
}

async Task UnavailableUpstreamIsReported()
{
    int port = Ports.FindUnused();
    await using GatewayFixture fixture = await GatewayFixture.StartAsync(_ => new GatewayOptions
    {
        ZoneId = "test",
        ZoneTcp = $"127.0.0.1:{port}",
        AllowedOrigins = [Origin],
        IdleTimeoutSeconds = 10,
    }, startEcho: false);
    using ClientWebSocket socket = await fixture.ConnectAsync();
    WebSocketCloseStatus status = await ReceiveUntilCloseAsync(socket, TimeSpan.FromSeconds(4));
    Require(status == WebSocketCloseStatus.EndpointUnavailable, $"Expected 1013, got {status}.");
}

static async Task ExpectConnectFailure(ClientWebSocket socket, Uri uri, string message)
{
    try
    {
        await socket.ConnectAsync(uri, CancellationToken.None);
        throw new InvalidOperationException(message);
    }
    catch (WebSocketException)
    {
    }
}

static async Task<byte[]> ReceiveBinaryAsync(ClientWebSocket socket, TimeSpan timeout)
{
    using CancellationTokenSource cts = new(timeout);
    byte[] buffer = new byte[4096];
    using MemoryStream message = new();
    while (true)
    {
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cts.Token);
        if (result.MessageType == WebSocketMessageType.Close)
            throw new InvalidOperationException($"Socket closed while waiting for binary data: {socket.CloseStatus}.");
        message.Write(buffer, 0, result.Count);
        if (result.EndOfMessage) return message.ToArray();
    }
}

static async Task<WebSocketCloseStatus> ReceiveUntilCloseAsync(ClientWebSocket socket, TimeSpan timeout)
{
    using CancellationTokenSource cts = new(timeout);
    byte[] buffer = new byte[4096];
    while (true)
    {
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cts.Token);
        if (result.MessageType == WebSocketMessageType.Close)
            return socket.CloseStatus ?? WebSocketCloseStatus.Empty;
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class GatewayFixture : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly EchoServer? _echo;

    private GatewayFixture(WebApplication app, string baseUrl, EchoServer? echo)
    {
        _app = app;
        BaseUrl = baseUrl;
        _echo = echo;
    }

    public string BaseUrl { get; }

    public static async Task<GatewayFixture> StartAsync(Func<GatewayOptions, GatewayOptions> configure, bool startEcho = true)
    {
        EchoServer? echo = startEcho ? await EchoServer.StartAsync() : null;
        int zonePort = echo?.Port ?? Ports.FindUnused();
        GatewayOptions options = configure(new GatewayOptions
        {
            ZoneId = "test",
            ZoneTcp = $"127.0.0.1:{zonePort}",
            AllowedOrigins = ["https://web.test"],
            IdleTimeoutSeconds = 10,
        });
        WebApplication app = GatewayApp.Build([], options);
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        string baseUrl = app.Urls.First().TrimEnd('/');
        return new GatewayFixture(app, baseUrl, echo);
    }

    public Uri WebSocketUri() => new(BaseUrl.Replace("http://", "ws://", StringComparison.Ordinal) + "/ws/game?zone=test");

    public async Task<ClientWebSocket> ConnectAsync()
    {
        ClientWebSocket socket = new();
        socket.Options.AddSubProtocol(GatewayOptions.Protocol);
        socket.Options.SetRequestHeader("Origin", "https://web.test");
        await socket.ConnectAsync(WebSocketUri(), CancellationToken.None);
        return socket;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (_echo is not null) await _echo.DisposeAsync();
    }
}

static class Ports
{
    public static int FindUnused()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

sealed class EchoServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    private EchoServer(TcpListener listener)
    {
        _listener = listener;
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static Task<EchoServer> StartAsync()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return Task.FromResult(new EchoServer(listener));
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = EchoAsync(client);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    private static async Task EchoAsync(TcpClient client)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4096];
            try
            {
                while (true)
                {
                    int read = await stream.ReadAsync(buffer);
                    if (read == 0) return;
                    await stream.WriteAsync(buffer.AsMemory(0, read));
                    await stream.FlushAsync();
                }
            }
            catch (IOException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
        }
        _cts.Dispose();
    }
}
