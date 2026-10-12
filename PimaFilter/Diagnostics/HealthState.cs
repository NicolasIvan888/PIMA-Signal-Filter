using System.Collections.Concurrent;
using PimaFilter.Configuration;

namespace PimaFilter.Diagnostics;

public sealed class HealthState
{
    private readonly ConcurrentDictionary<int, RouteCounters> _routes = new();
    private long _activeConnections;
    private long _totalConnections;
    private long _allowed;
    private long _blocked;
    private long _wouldBlock;
    private long _errors;
    private long _acksRelayed;
    private long _acksGenerated;
    private long _probesAccepted;
    private long _invalidFrames;
    private long _lastMessageUnixMilliseconds;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    public void RegisterRoute(int port, string name, string frameFormat = "Andromeda38", bool filterEnabled = true) =>
        _routes.TryAdd(port, new RouteCounters(name, frameFormat, filterEnabled));

    public void ConnectionOpened(int port)
    {
        Interlocked.Increment(ref _activeConnections);
        Interlocked.Increment(ref _totalConnections);
        _routes.GetOrAdd(port, _ => new RouteCounters()).ConnectionOpened();
    }

    public void ConnectionClosed(int port)
    {
        Interlocked.Decrement(ref _activeConnections);
        _routes.GetOrAdd(port, _ => new RouteCounters()).ConnectionClosed();
    }

    public void MessageReceived(int port)
    {
        Interlocked.Exchange(
            ref _lastMessageUnixMilliseconds,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _routes.GetOrAdd(port, _ => new RouteCounters()).MessageReceived();
    }

    public void Allowed(int port)
    {
        Interlocked.Increment(ref _allowed);
        _routes.GetOrAdd(port, _ => new RouteCounters()).Allowed();
    }

    public void Blocked(int port)
    {
        Interlocked.Increment(ref _blocked);
        Interlocked.Increment(ref _acksGenerated);
        _routes.GetOrAdd(port, _ => new RouteCounters()).Blocked();
    }

    public void WouldBlock(int port)
    {
        Interlocked.Increment(ref _wouldBlock);
        _routes.GetOrAdd(port, _ => new RouteCounters()).WouldBlock();
    }

    public void AckRelayed() => Interlocked.Increment(ref _acksRelayed);
    public void ProbeAccepted(int port)
    {
        Interlocked.Increment(ref _acksGenerated);
        Interlocked.Increment(ref _probesAccepted);
        Allowed(port);
    }
    public void Error() => Interlocked.Increment(ref _errors);
    public void InvalidFrame() => Interlocked.Increment(ref _invalidFrames);

    public object Snapshot(string mode, int blacklistCount, DiagnosticsOptions diagnostics) => new
    {
        timestampLocal = DateTimeOffset.Now,
        startedAt = StartedAt,
        mode,
        blacklistCount,
        activeConnections = Interlocked.Read(ref _activeConnections),
        totalConnections = Interlocked.Read(ref _totalConnections),
        allowed = Interlocked.Read(ref _allowed),
        blocked = Interlocked.Read(ref _blocked),
        wouldBlock = Interlocked.Read(ref _wouldBlock),
        errors = Interlocked.Read(ref _errors),
        acksRelayed = Interlocked.Read(ref _acksRelayed),
        acksGenerated = Interlocked.Read(ref _acksGenerated),
        probesAccepted = Interlocked.Read(ref _probesAccepted),
        invalidFrames = Interlocked.Read(ref _invalidFrames),
        logSampling = new
        {
            allowedEveryN = diagnostics.AllowedLogEveryN,
            blockedEveryN = diagnostics.BlockedLogEveryN,
            errorEveryN = diagnostics.ErrorLogEveryN
        },
        lastMessageAt = ReadLastMessage(),
        routes = _routes
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.Snapshot())
    };

    private DateTimeOffset? ReadLastMessage()
    {
        var value = Interlocked.Read(ref _lastMessageUnixMilliseconds);
        return value == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime();
    }

    private sealed class RouteCounters
    {
        private readonly string _name;
        private readonly string _frameFormat;
        private readonly bool _filterEnabled;

        public RouteCounters(string name = "", string frameFormat = "Andromeda38", bool filterEnabled = true)
        {
            _name = name;
            _frameFormat = frameFormat;
            _filterEnabled = filterEnabled;
        }
        private long _active;
        private long _connections;
        private long _messages;
        private long _allowed;
        private long _blocked;
        private long _wouldBlock;

        public void ConnectionOpened()
        {
            Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _connections);
        }

        public void ConnectionClosed() => Interlocked.Decrement(ref _active);
        public void MessageReceived() => Interlocked.Increment(ref _messages);
        public void Allowed() => Interlocked.Increment(ref _allowed);
        public void Blocked() => Interlocked.Increment(ref _blocked);
        public void WouldBlock() => Interlocked.Increment(ref _wouldBlock);

        public object Snapshot() => new
        {
            name = _name,
            frameFormat = _frameFormat,
            filterEnabled = _filterEnabled,
            activeConnections = Interlocked.Read(ref _active),
            totalConnections = Interlocked.Read(ref _connections),
            messages = Interlocked.Read(ref _messages),
            allowed = Interlocked.Read(ref _allowed),
            blocked = Interlocked.Read(ref _blocked),
            wouldBlock = Interlocked.Read(ref _wouldBlock)
        };
    }
}
