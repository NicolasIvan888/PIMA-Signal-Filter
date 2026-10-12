namespace PimaFilter.Rules;

public sealed class BlacklistReloadWorker(
    BlacklistProvider provider,
    ILogger<BlacklistReloadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await provider.ReloadIfChangedAsync(stoppingToken);
            logger.LogDebug("Blacklist activ: {Count} coduri.", provider.Count);
        }
    }
}
