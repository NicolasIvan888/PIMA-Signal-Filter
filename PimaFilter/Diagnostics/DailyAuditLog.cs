using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PimaFilter.Configuration;

namespace PimaFilter.Diagnostics;

public sealed class DailyAuditLog
{
    private readonly string _directory;
    private readonly int _allowedEveryN;
    private readonly int _blockedEveryN;
    private readonly int _errorEveryN;
    private readonly ConcurrentDictionary<string, EventCounter> _allowedSeen = new();
    private readonly ConcurrentDictionary<string, EventCounter> _blockedSeen = new();
    private readonly ConcurrentDictionary<string, EventCounter> _errorSeen = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DailyAuditLog(IOptions<AppOptions> options)
    {
        _directory = ResolvePath(options.Value.Diagnostics.LogDirectory);
        _allowedEveryN = options.Value.Diagnostics.AllowedLogEveryN;
        _blockedEveryN = options.Value.Diagnostics.BlockedLogEveryN;
        _errorEveryN = options.Value.Diagnostics.ErrorLogEveryN;
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public Task WriteAllowedAsync(object details, CancellationToken cancellationToken, string routeName = "") =>
        ShouldWrite(_allowedSeen.GetOrAdd(routeName, _ => new EventCounter()).Increment(), _allowedEveryN)
            ? WriteAsync("allowed", details, cancellationToken)
            : Task.CompletedTask;

    public Task WriteBlockedAsync(object details, CancellationToken cancellationToken, string routeName = "") =>
        ShouldWrite(_blockedSeen.GetOrAdd(routeName, _ => new EventCounter()).Increment(), _blockedEveryN)
            ? WriteAsync("blocked", details, cancellationToken)
            : Task.CompletedTask;

    public Task WriteInvalidAsync(object details, CancellationToken cancellationToken) =>
        WriteAsync("invalid", details, cancellationToken);

    public Task WriteErrorAsync(string eventType, object details, CancellationToken cancellationToken)
    {
        var count = _errorSeen.GetOrAdd(eventType, _ => new EventCounter()).Increment();
        return ShouldWrite(count, _errorEveryN)
            ? WriteAsync("errors", new { eventType, details }, cancellationToken)
            : Task.CompletedTask;
    }

    private static bool ShouldWrite(long count, int everyN) =>
        count == 1 || count % everyN == 0;

    private sealed class EventCounter
    {
        private long _count;
        public long Increment() => Interlocked.Increment(ref _count);
    }

    private async Task WriteAsync(string category, object details, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var line = JsonSerializer.Serialize(
                new
                {
                    timestampLocal = now,
                    timestampUtc = now.ToUniversalTime(),
                    details
                },
                _jsonOptions);
            var path = Path.Combine(_directory, $"{category}-{now:yyyy-MM-dd}.jsonl");

            await _gate.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Logging must never interrupt alarm delivery.
            System.Diagnostics.Trace.WriteLine(
                $"PIMA Signal Filter log failure ({category}): {exception}");
        }
    }

    private static string ResolvePath(string configuredPath) =>
        Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath));
}
