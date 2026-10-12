using System.Text.Json;
using Microsoft.Extensions.Options;
using PimaFilter.Configuration;
using PimaFilter.Diagnostics;

namespace PimaFilter.Rules;

public sealed class BlacklistProvider
{
    private readonly string _path;
    private readonly DailyAuditLog _auditLog;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private IReadOnlyDictionary<string, BlacklistRule> _rules =
        new Dictionary<string, BlacklistRule>(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public BlacklistProvider(IOptions<AppOptions> options, DailyAuditLog auditLog)
    {
        _path = ResolvePath(options.Value.BlacklistFile);
        _auditLog = auditLog;
    }

    public string Path => _path;
    public int Count => Volatile.Read(ref _rules).Count;

    public bool TryGetRule(string objectCode, out BlacklistRule? rule) =>
        Volatile.Read(ref _rules).TryGetValue(objectCode, out rule);

    public async Task ReloadIfChangedAsync(CancellationToken cancellationToken)
    {
        var currentWrite = File.Exists(_path)
            ? File.GetLastWriteTimeUtc(_path)
            : DateTime.MinValue;

        if (currentWrite != _lastWriteUtc)
        {
            await ReloadAsync(cancellationToken);
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                await File.WriteAllTextAsync(
                    _path,
                    "{\n  \"entries\": []\n}\n",
                    cancellationToken);
            }

            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            var document = JsonSerializer.Deserialize<BlacklistDocument>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new BlacklistDocument();

            var next = new Dictionary<string, BlacklistRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in document.Entries.Where(item => item.Enabled))
            {
                var code = entry.ObjectCode.Trim().ToUpperInvariant();
                if (code.Length != 6 || code.Any(character => !char.IsAsciiLetterOrDigit(character)))
                {
                    throw new InvalidDataException(
                        $"Cod blacklist invalid: '{entry.ObjectCode}'. Sunt necesare sase caractere alfanumerice.");
                }

                next[code] = new BlacklistRule(code, entry.Reason.Trim(), entry.Note.Trim());
            }

            Volatile.Write(ref _rules, next);
            _lastWriteUtc = File.GetLastWriteTimeUtc(_path);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Fail open: a damaged blacklist must never stop valid alarm traffic.
            Volatile.Write(
                ref _rules,
                new Dictionary<string, BlacklistRule>(StringComparer.OrdinalIgnoreCase));
            _lastWriteUtc = File.Exists(_path)
                ? File.GetLastWriteTimeUtc(_path)
                : DateTime.MinValue;
            await _auditLog.WriteErrorAsync(
                "blacklist-reload-failed",
                new { path = _path, exception.Message },
                cancellationToken);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private static string ResolvePath(string configuredPath) =>
        System.IO.Path.GetFullPath(
            System.IO.Path.IsPathRooted(configuredPath)
                ? configuredPath
                : System.IO.Path.Combine(AppContext.BaseDirectory, configuredPath));
}
