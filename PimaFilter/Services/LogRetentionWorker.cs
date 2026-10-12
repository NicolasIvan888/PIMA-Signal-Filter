using Microsoft.Extensions.Options;
using PimaFilter.Configuration;
using PimaFilter.Diagnostics;

namespace PimaFilter.Services;

public sealed class LogRetentionWorker(
    IOptionsMonitor<AppOptions> optionsMonitor,
    DailyAuditLog auditLog,
    HealthState health) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var diagnostics = optionsMonitor.CurrentValue.Diagnostics;
            DeleteOlderThan("allowed-*.jsonl", diagnostics.AllowedRetentionDays);
            DeleteOlderThan("blocked-*.jsonl", diagnostics.BlockedRetentionDays);
            DeleteOlderThan("errors-*.jsonl", diagnostics.ErrorRetentionDays);
            DeleteOlderThan("invalid-*.jsonl", diagnostics.InvalidRetentionDays);
        }
        catch (Exception exception)
        {
            health.Error();
            await auditLog.WriteErrorAsync(
                "log-retention-failed",
                new { exception.Message },
                cancellationToken);
        }
    }

    private void DeleteOlderThan(string pattern, int days)
    {
        var threshold = DateTime.UtcNow.AddDays(-days);
        foreach (var path in Directory.EnumerateFiles(auditLog.DirectoryPath, pattern))
        {
            if (File.GetLastWriteTimeUtc(path) < threshold)
            {
                File.Delete(path);
            }
        }
    }
}
