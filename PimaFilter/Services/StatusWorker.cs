using System.Text.Json;
using Microsoft.Extensions.Options;
using PimaFilter.Configuration;
using PimaFilter.Diagnostics;
using PimaFilter.Rules;

namespace PimaFilter.Services;

public sealed class StatusWorker(
    IOptionsMonitor<AppOptions> optionsMonitor,
    HealthState health,
    BlacklistProvider blacklist,
    DailyAuditLog auditLog) : BackgroundService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var options = optionsMonitor.CurrentValue;
                var path = ResolvePath(options.Diagnostics.StatusFile);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonSerializer.Serialize(
                    health.Snapshot(options.Mode, blacklist.Count, options.Diagnostics),
                    _jsonOptions);
                var temporary = path + ".tmp";
                await File.WriteAllTextAsync(temporary, json, stoppingToken);
                File.Move(temporary, path, overwrite: true);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                health.Error();
                await auditLog.WriteErrorAsync(
                    "status-write-failed",
                    new { exception.Message },
                    CancellationToken.None);
            }

            await Task.Delay(
                TimeSpan.FromSeconds(optionsMonitor.CurrentValue.Diagnostics.StatusIntervalSeconds),
                stoppingToken);
        }
    }

    private static string ResolvePath(string configuredPath) =>
        Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath));
}
