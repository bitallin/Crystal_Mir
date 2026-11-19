using System.Diagnostics;
using System.Text.Json;

namespace Crystal.Web.Gateway;

public static class GatewayMetrics
{
    private static readonly long StartedAt = Stopwatch.GetTimestamp();
    private static long _connectionsAccepted;
    private static long _connectionsRejected;
    private static long _originRejected;
    private static long _upstreamFailures;
    private static long _bytesFromClient;
    private static long _bytesFromZone;
    private static long _framesFromClient;
    private static long _framesFromZone;
    private static long _oversizedFrames;
    private static long _rateLimited;
    private static long _idleTimeouts;
    private static long _upstreamDisconnects;
    private static long _activeConnections;

    public static long ActiveConnections => Interlocked.Read(ref _activeConnections);

    public static void Accepted() => Interlocked.Increment(ref _connectionsAccepted);
    public static void Rejected() => Interlocked.Increment(ref _connectionsRejected);
    public static void OriginRejected() => Interlocked.Increment(ref _originRejected);
    public static void UpstreamFailed() => Interlocked.Increment(ref _upstreamFailures);
    public static void ClientBytes(int count) => Interlocked.Add(ref _bytesFromClient, count);
    public static void ZoneBytes(int count) => Interlocked.Add(ref _bytesFromZone, count);
    public static void ClientFrame() => Interlocked.Increment(ref _framesFromClient);
    public static void ZoneFrame() => Interlocked.Increment(ref _framesFromZone);
    public static void OversizedFrame() => Interlocked.Increment(ref _oversizedFrames);
    public static void RateLimited() => Interlocked.Increment(ref _rateLimited);
    public static void IdleTimeout() => Interlocked.Increment(ref _idleTimeouts);
    public static void UpstreamDisconnected() => Interlocked.Increment(ref _upstreamDisconnects);
    public static void ConnectionOpened() => Interlocked.Increment(ref _activeConnections);
    public static void ConnectionClosed() => Interlocked.Decrement(ref _activeConnections);

    public static object Snapshot() => new
    {
        activeConnections = ActiveConnections,
        connectionsAccepted = Interlocked.Read(ref _connectionsAccepted),
        connectionsRejected = Interlocked.Read(ref _connectionsRejected),
        originRejected = Interlocked.Read(ref _originRejected),
        upstreamFailures = Interlocked.Read(ref _upstreamFailures),
        bytesFromClient = Interlocked.Read(ref _bytesFromClient),
        bytesFromZone = Interlocked.Read(ref _bytesFromZone),
        framesFromClient = Interlocked.Read(ref _framesFromClient),
        framesFromZone = Interlocked.Read(ref _framesFromZone),
        oversizedFrames = Interlocked.Read(ref _oversizedFrames),
        rateLimited = Interlocked.Read(ref _rateLimited),
        idleTimeouts = Interlocked.Read(ref _idleTimeouts),
        upstreamDisconnects = Interlocked.Read(ref _upstreamDisconnects),
        uptimeSeconds = (long)Stopwatch.GetElapsedTime(StartedAt).TotalSeconds,
    };

    public static string SnapshotJson() => JsonSerializer.Serialize(Snapshot());
}
