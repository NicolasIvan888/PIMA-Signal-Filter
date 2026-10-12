using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PimaFilter.Configuration;
using PimaFilter.Diagnostics;
using PimaFilter.Rules;
using PimaFilter.Services;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PimaFilter";
});

builder.Services
    .AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection("PimaFilter"))
    .Validate(options =>
    {
        if (!IPAddress.TryParse(options.BindAddress, out _)
            || !FilterModeParser.TryParse(options.Mode, out _)
            || options.Routes.Count == 0
            || options.MaxConcurrentConnections is < 1 or > 10_000
            || options.ClientReadTimeoutSeconds is < 1 or > 300
            || options.UpstreamConnectTimeoutSeconds is < 1 or > 60
            || options.UpstreamAckTimeoutSeconds is < 1 or > 60
            || options.MaxFrameBytes is < 38 or > 65_536
            || string.IsNullOrWhiteSpace(options.BlacklistFile)
            || string.IsNullOrWhiteSpace(options.Diagnostics.LogDirectory)
            || string.IsNullOrWhiteSpace(options.Diagnostics.StatusFile)
            || options.Diagnostics.StatusIntervalSeconds is < 1 or > 300
            || options.Diagnostics.AllowedRetentionDays is < 1 or > 365
            || options.Diagnostics.BlockedRetentionDays is < 1 or > 3_650
            || options.Diagnostics.ErrorRetentionDays is < 1 or > 3_650
            || options.Diagnostics.InvalidRetentionDays is < 1 or > 3_650
            || options.Diagnostics.AllowedLogEveryN is < 1 or > 1_000_000
            || options.Diagnostics.BlockedLogEveryN is < 1 or > 1_000_000
            || options.Diagnostics.ErrorLogEveryN is < 1 or > 1_000_000)
        {
            return false;
        }

        var ports = new HashSet<int>();
        foreach (var route in options.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.Name)
                || route.ListenPort is < 1 or > 65_535
                || route.UpstreamPort is < 1 or > 65_535
                || string.IsNullOrWhiteSpace(route.UpstreamHost)
                || route.FrameFormat is not ("Andromeda38" or "Atia13")
                || (route.FilterEnabled && !Regex.IsMatch(route.ObjectPrefix, "^[0-9]{2}$"))
                || (!route.FilterEnabled && route.ObjectPrefix.Length > 0
                    && !Regex.IsMatch(route.ObjectPrefix, "^[0-9]{2}$"))
                || (!string.IsNullOrWhiteSpace(route.ObjectCodeBase)
                    && !Regex.IsMatch(route.ObjectCodeBase, "^[0-9]{6}$"))
                || (route.NumericObjectDigits is < 1 or > 5)
                || !ports.Add(route.ListenPort))
            {
                return false;
            }
        }

        return true;
    }, "Configuratia PIMA Signal Filter este invalida.")
    .ValidateOnStart();

builder.Services.AddSingleton<DailyAuditLog>();
builder.Services.AddSingleton<HealthState>();
builder.Services.AddSingleton<BlacklistProvider>();
builder.Services.AddHostedService<BlacklistReloadWorker>();
builder.Services.AddHostedService<PimaProxyWorker>();
builder.Services.AddHostedService<StatusWorker>();
builder.Services.AddHostedService<LogRetentionWorker>();

var host = builder.Build();

_ = host.Services.GetRequiredService<IOptions<AppOptions>>().Value;
await host.Services.GetRequiredService<BlacklistProvider>().ReloadAsync(CancellationToken.None);

await host.RunAsync();
