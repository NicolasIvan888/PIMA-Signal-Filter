using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using PimaFilter.Configuration;
using PimaFilter.Diagnostics;
using PimaFilter.Protocol;
using PimaFilter.Rules;

namespace PimaFilter.Services;

public sealed class PimaProxyWorker : BackgroundService
{
    private const byte Ack = 0x06;
    private readonly IOptionsMonitor<AppOptions> _optionsMonitor;
    private readonly AppOptions _startupOptions;
    private readonly BlacklistProvider _blacklist;
    private readonly DailyAuditLog _auditLog;
    private readonly HealthState _health;
    private readonly ILogger<PimaProxyWorker> _logger;
    private readonly List<TcpListener> _listeners = [];
    private readonly SemaphoreSlim _connectionLimit;

    public PimaProxyWorker(
        IOptionsMonitor<AppOptions> optionsMonitor,
        BlacklistProvider blacklist,
        DailyAuditLog auditLog,
        HealthState health,
        ILogger<PimaProxyWorker> logger)
    {
        _optionsMonitor = optionsMonitor;
        _startupOptions = optionsMonitor.CurrentValue;
        _blacklist = blacklist;
        _auditLog = auditLog;
        _health = health;
        _logger = logger;
        _connectionLimit = new SemaphoreSlim(
            _startupOptions.MaxConcurrentConnections,
            _startupOptions.MaxConcurrentConnections);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bindAddress = IPAddress.Parse(_startupOptions.BindAddress);
        var loops = new List<Task>();

        foreach (var route in _startupOptions.Routes)
        {
            _health.RegisterRoute(route.ListenPort, route.Name, route.FrameFormat, route.FilterEnabled);
            var listener = new TcpListener(bindAddress, route.ListenPort);
            listener.Start(512);
            _listeners.Add(listener);
            _logger.LogInformation(
                "Ascultare {BindAddress}:{ListenPort}; destinatie {Host}:{Port}; format {Format}; filtru {FilterEnabled}; prefix {Prefix}.",
                bindAddress,
                route.ListenPort,
                route.UpstreamHost,
                route.UpstreamPort,
                route.FrameFormat,
                route.FilterEnabled,
                route.ObjectPrefix);
            loops.Add(AcceptLoopAsync(listener, route, stoppingToken));
        }

        await Task.WhenAll(loops);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var listener in _listeners)
        {
            listener.Stop();
        }

        return base.StopAsync(cancellationToken);
    }

    private async Task AcceptLoopAsync(
        TcpListener listener,
        RouteOptions route,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(stoppingToken);
                await _connectionLimit.WaitAsync(stoppingToken);
                _ = HandleConnectionGuardedAsync(client, route, stoppingToken);
                client = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                client?.Dispose();
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                client?.Dispose();
                break;
            }
            catch (Exception exception)
            {
                client?.Dispose();
                _health.Error();
                await _auditLog.WriteErrorAsync(
                    "accept-failed",
                    new { route = route.Name, route.ListenPort, exception.Message },
                    CancellationToken.None);
                await Task.Delay(250, stoppingToken);
            }
        }
    }

    private async Task HandleConnectionGuardedAsync(
        TcpClient client,
        RouteOptions route,
        CancellationToken stoppingToken)
    {
        try
        {
            await HandleConnectionAsync(client, route, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        catch (Exception exception)
        {
            _health.Error();
            await _auditLog.WriteErrorAsync(
                "client-handler-failed",
                new
                {
                    route = route.Name,
                    route.ListenPort,
                    remoteEndpoint = client.Client.RemoteEndPoint?.ToString(),
                    exception.Message,
                    exceptionType = exception.GetType().FullName
                },
                CancellationToken.None);
        }
        finally
        {
            client.Dispose();
            _connectionLimit.Release();
        }
    }

    private async Task HandleConnectionAsync(
        TcpClient client,
        RouteOptions route,
        CancellationToken stoppingToken)
    {
        client.NoDelay = true;
        var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _health.ConnectionOpened(route.ListenPort);

        try
        {
            using var stream = client.GetStream();
            var reader = new PimaFrameReader();

            while (!stoppingToken.IsCancellationRequested)
            {
                byte[]? frame;
                using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(
                        _optionsMonitor.CurrentValue.ClientReadTimeoutSeconds));
                    try
                    {
                        frame = await reader.ReadFrameAsync(
                            stream,
                            _optionsMonitor.CurrentValue.MaxFrameBytes,
                            readTimeout.Token);
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        await _auditLog.WriteErrorAsync(
                            "client-read-timeout",
                            new { route = route.Name, route.ListenPort, remoteEndpoint },
                            CancellationToken.None);
                        break;
                    }
                }

                if (frame is null)
                {
                    break;
                }

                _health.MessageReceived(route.ListenPort);
                var message = PimaMessage.Parse(
                    frame,
                    route.ObjectPrefix,
                    route.FrameFormat,
                    route.ObjectCodeBase,
                    route.NumericObjectDigits);
                if (message.IsCommunicationProbe)
                {
                    await stream.WriteAsync(new byte[] { Ack }, stoppingToken);
                    _health.ProbeAccepted(route.ListenPort);
                    continue;
                }

                if (!message.IsValid)
                {
                    _health.InvalidFrame();
                    await _auditLog.WriteInvalidAsync(
                        new
                        {
                            route = route.Name,
                            route.ListenPort,
                            remoteEndpoint,
                            message.ValidationError,
                            frameLength = frame.Length,
                            frameHex = message.Hex,
                            frameAscii = message.Ascii.Replace("\n", "<LF>").Replace("\r", "<CR>")
                        },
                        stoppingToken);
                }

                BlacklistRule? rule = null;
                var hasRule = route.FilterEnabled && message.ObjectCode is not null
                    && _blacklist.TryGetRule(message.ObjectCode, out rule);
                var mode = FilterModeParser.Parse(_optionsMonitor.CurrentValue.Mode);

                if (mode == FilterMode.Monitor && hasRule)
                {
                    _health.WouldBlock(route.ListenPort);
                }

                if (mode == FilterMode.Block && hasRule)
                {
                    await stream.WriteAsync(new byte[] { Ack }, stoppingToken);
                    _health.Blocked(route.ListenPort);
                    await _auditLog.WriteBlockedAsync(
                        CreateAuditDetails(
                            route,
                            remoteEndpoint,
                            message,
                            "blocked",
                            rule,
                            null),
                        stoppingToken,
                        route.Name);
                    continue;
                }

                var relay = await RelayToUpstreamAsync(route, frame, stoppingToken);
                if (!relay.Acknowledged)
                {
                    _health.Error();
                    await _auditLog.WriteErrorAsync(
                        "upstream-not-acknowledged",
                        new
                        {
                            route = route.Name,
                            route.ListenPort,
                            remoteEndpoint,
                            message.ObjectCode,
                            message.RawObjectCode,
                            message.ValidationError,
                            frameLength = frame.Length,
                            frameHex = message.Hex,
                            frameAscii = message.Ascii.Replace("\n", "<LF>").Replace("\r", "<CR>"),
                            relay.Error,
                            relay.ResponseByte,
                            relay.ElapsedMilliseconds
                        },
                        CancellationToken.None);
                    break;
                }

                await stream.WriteAsync(new byte[] { Ack }, stoppingToken);
                _health.AckRelayed();
                _health.Allowed(route.ListenPort);
                await _auditLog.WriteAllowedAsync(
                    CreateAuditDetails(
                        route,
                        remoteEndpoint,
                        message,
                        hasRule ? "monitor-would-block" : "allowed",
                        rule,
                        relay.ElapsedMilliseconds),
                    stoppingToken,
                    route.Name);
            }
        }
        finally
        {
            _health.ConnectionClosed(route.ListenPort);
        }
    }

    private async Task<RelayResult> RelayToUpstreamAsync(
        RouteOptions route,
        byte[] frame,
        CancellationToken stoppingToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var upstream = new TcpClient { NoDelay = true };
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(
                    _optionsMonitor.CurrentValue.UpstreamConnectTimeoutSeconds));
                await upstream.ConnectAsync(
                    route.UpstreamHost,
                    route.UpstreamPort,
                    connectTimeout.Token);
            }

            using var stream = upstream.GetStream();
            await stream.WriteAsync(frame, stoppingToken);

            var response = new byte[1];
            int read;
            using (var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                ackTimeout.CancelAfter(TimeSpan.FromSeconds(
                    _optionsMonitor.CurrentValue.UpstreamAckTimeoutSeconds));
                read = await stream.ReadAsync(response, ackTimeout.Token);
            }

            stopwatch.Stop();
            return read == 1 && response[0] == Ack
                ? new RelayResult(true, response[0], stopwatch.Elapsed.TotalMilliseconds, null)
                : new RelayResult(
                    false,
                    read == 1 ? response[0] : null,
                    stopwatch.Elapsed.TotalMilliseconds,
                    read == 0 ? "EvMan a inchis conexiunea fara ACK." : "Raspunsul EvMan nu este 06.");
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new RelayResult(false, null, stopwatch.Elapsed.TotalMilliseconds, "Timeout EvMan.");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new RelayResult(false, null, stopwatch.Elapsed.TotalMilliseconds, exception.Message);
        }
    }

    private static object CreateAuditDetails(
        RouteOptions route,
        string remoteEndpoint,
        PimaMessage message,
        string action,
        BlacklistRule? rule,
        double? upstreamMilliseconds) => new
    {
        action,
        route = route.Name,
        listenPort = route.ListenPort,
        remoteEndpoint,
        upstream = $"{route.UpstreamHost}:{route.UpstreamPort}",
        message.RawObjectCode,
        message.ObjectCode,
        message.EventField,
        message.Qualifier,
        message.Checksum,
        message.IsValid,
        message.ValidationError,
        frameAscii = message.Ascii.Replace("\n", "<LF>").Replace("\r", "<CR>"),
        frameHex = message.Hex,
        blacklistReason = rule?.Reason,
        blacklistNote = rule?.Note,
        upstreamMilliseconds
    };

    private sealed record RelayResult(
        bool Acknowledged,
        byte? ResponseByte,
        double ElapsedMilliseconds,
        string? Error);
}
