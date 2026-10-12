namespace PimaFilter.Configuration;

public sealed class AppOptions
{
    public string Mode { get; set; } = "Monitor";
    public string BindAddress { get; set; } = "127.0.0.1";
    public int MaxConcurrentConnections { get; set; } = 256;
    public int ClientReadTimeoutSeconds { get; set; } = 10;
    public int UpstreamConnectTimeoutSeconds { get; set; } = 3;
    public int UpstreamAckTimeoutSeconds { get; set; } = 4;
    public int MaxFrameBytes { get; set; } = 512;
    public string BlacklistFile { get; set; } = "data/config/blacklist.json";
    public List<RouteOptions> Routes { get; set; } = [];
    public DiagnosticsOptions Diagnostics { get; set; } = new();
}

public sealed class RouteOptions
{
    public string Name { get; set; } = "";
    public int ListenPort { get; set; }
    public string UpstreamHost { get; set; } = "";
    public int UpstreamPort { get; set; }
    public string ObjectPrefix { get; set; } = "";
    // Optional six-digit numeric base used by routes where EvMan creates the
    // final account by adding the real object number.
    public string ObjectCodeBase { get; set; } = "";
    // Number of significant digits at the right of a numeric account field.
    // Atia13 Example-Atia carries a five-character field whose first digit is not
    // part of the account number.
    public int NumericObjectDigits { get; set; } = 5;
    public string FrameFormat { get; set; } = "Andromeda38";
    public bool FilterEnabled { get; set; } = true;
    public List<string> AllowedSourceAddresses { get; set; } = [];
}

public sealed class DiagnosticsOptions
{
    public string LogDirectory { get; set; } = "data/logs";
    public string StatusFile { get; set; } = "data/status.json";
    public int StatusIntervalSeconds { get; set; } = 5;
    public int AllowedRetentionDays { get; set; } = 1;
    public int BlockedRetentionDays { get; set; } = 7;
    public int ErrorRetentionDays { get; set; } = 30;
    public int InvalidRetentionDays { get; set; } = 7;
    public int AllowedLogEveryN { get; set; } = 1000;
    public int BlockedLogEveryN { get; set; } = 100;
    public int ErrorLogEveryN { get; set; } = 1;
}

public enum FilterMode
{
    Monitor,
    Block
}

public static class FilterModeParser
{
    public static bool TryParse(string? value, out FilterMode mode) =>
        Enum.TryParse(value, ignoreCase: true, out mode);

    public static FilterMode Parse(string? value) =>
        TryParse(value, out var mode) ? mode : FilterMode.Monitor;
}
