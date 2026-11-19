using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace Crystal.Web.Gateway;

public static class WebSocketBridge
{
    public static async Task RunAsync(
        WebSocket socket,
        IPEndPoint upstream,
        GatewayOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using TcpClient client = new() { NoDelay = true };
        try
        {
            await client.ConnectAsync(upstream, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            GatewayMetrics.UpstreamFailed();
            logger.LogWarning(exception, "Zone upstream {Upstream} is unavailable.", upstream);
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.EndpointUnavailable, "zone unavailable", cancellationToken);
            return;
        }

        using NetworkStream upstreamStream = client.GetStream();
        using CancellationTokenSource bridgeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ActivityClock activity = new();
        ClientRateLimiter rateLimiter = new(options.MaxFramesPerSecond, options.MaxBytesPerSecond);

        Task wsToTcp = PumpWebSocketToTcpAsync(socket, upstreamStream, options, activity, rateLimiter, bridgeCts.Token);
        Task tcpToWs = PumpTcpToWebSocketAsync(upstreamStream, socket, activity, bridgeCts.Token);
        Task watchdog = WatchIdleAsync(activity, options.IdleTimeoutSeconds, bridgeCts.Token);

        Task completed = await Task.WhenAny(wsToTcp, tcpToWs, watchdog);
        if (completed == watchdog)
        {
            GatewayMetrics.IdleTimeout();
            logger.LogInformation("Closing idle web gateway connection.");
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "idle timeout", CancellationToken.None);
        }

        bridgeCts.Cancel();
        await AwaitAllAsync(wsToTcp, tcpToWs, watchdog);

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
    }

    private static async Task PumpWebSocketToTcpAsync(
        WebSocket socket,
        Stream upstream,
        GatewayOptions options,
        ActivityClock activity,
        ClientRateLimiter rateLimiter,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream message = new();

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    await CloseQuietlyAsync(socket, WebSocketCloseStatus.InvalidMessageType, "binary required", CancellationToken.None);
                    return;
                }

                if (message.Length + result.Count > options.MaxFrameBytes)
                {
                    GatewayMetrics.OversizedFrame();
                    await CloseQuietlyAsync(socket, WebSocketCloseStatus.MessageTooBig, "frame too large", CancellationToken.None);
                    return;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                int frameLength = (int)message.Length;
                if (!rateLimiter.TryConsume(frameLength))
                {
                    GatewayMetrics.RateLimited();
                    await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "rate limit", CancellationToken.None);
                    return;
                }
                GatewayMetrics.ClientFrame();
                GatewayMetrics.ClientBytes(frameLength);
                await upstream.WriteAsync(message.GetBuffer().AsMemory(0, (int)message.Length), cancellationToken);
                await upstream.FlushAsync(cancellationToken);
                message.SetLength(0);
                activity.Touch();
            }
        }
        catch (Exception exception) when (exception is WebSocketException or IOException or OperationCanceledException)
        {
        }
    }

    private static async Task PumpTcpToWebSocketAsync(
        Stream upstream,
        WebSocket socket,
        ActivityClock activity,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                int read = await upstream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    GatewayMetrics.UpstreamDisconnected();
                    break;
                }

                GatewayMetrics.ZoneFrame();
                GatewayMetrics.ZoneBytes(read);
                await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, true, cancellationToken);
                activity.Touch();
            }
        }
        catch (Exception exception) when (exception is WebSocketException or IOException or OperationCanceledException)
        {
        }
    }

    private static async Task WatchIdleAsync(ActivityClock activity, int timeoutSeconds, CancellationToken cancellationToken)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds / 4.0, 1, 5)), cancellationToken);
            if (activity.IdleFor >= timeout) return;
        }
    }

    private static async Task CloseQuietlyAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string description,
        CancellationToken cancellationToken)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        try
        {
            await socket.CloseAsync(status, description, cancellationToken);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
        }
    }

    private static async Task AwaitAllAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or IOException)
        {
        }
    }

    private sealed class ActivityClock
    {
        private long _lastActivity = Environment.TickCount64;
        public TimeSpan IdleFor => TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastActivity));
        public void Touch() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
    }

    private sealed class ClientRateLimiter
    {
        private readonly int _maxFrames;
        private readonly int _maxBytes;
        private long _windowStarted = Environment.TickCount64;
        private int _frames;
        private long _bytes;

        public ClientRateLimiter(int maxFrames, int maxBytes)
        {
            _maxFrames = Math.Max(1, maxFrames);
            _maxBytes = Math.Max(1, maxBytes);
        }

        public bool TryConsume(int bytes)
        {
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _windowStarted) >= 1000)
            {
                Interlocked.Exchange(ref _windowStarted, now);
                Interlocked.Exchange(ref _frames, 0);
                Interlocked.Exchange(ref _bytes, 0);
            }

            int frames = Interlocked.Increment(ref _frames);
            long totalBytes = Interlocked.Add(ref _bytes, Math.Max(0, bytes));
            return frames <= _maxFrames && totalBytes <= _maxBytes;
        }
    }
}
