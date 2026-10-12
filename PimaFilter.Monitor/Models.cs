using System.Text.Json.Serialization;

namespace PimaFilter.Monitor;

public sealed class FilterStatus
{
    [JsonPropertyName("timestampLocal")]
    public DateTimeOffset TimestampLocal { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Necunoscut";

    [JsonPropertyName("blacklistCount")]
    public long BlacklistCount { get; set; }

    [JsonPropertyName("activeConnections")]
    public long ActiveConnections { get; set; }

    [JsonPropertyName("totalConnections")]
    public long TotalConnections { get; set; }

    [JsonPropertyName("allowed")]
    public long Allowed { get; set; }

    [JsonPropertyName("blocked")]
    public long Blocked { get; set; }

    [JsonPropertyName("wouldBlock")]
    public long WouldBlock { get; set; }

    [JsonPropertyName("errors")]
    public long Errors { get; set; }

    [JsonPropertyName("acksRelayed")]
    public long AcksRelayed { get; set; }

    [JsonPropertyName("acksGenerated")]
    public long AcksGenerated { get; set; }

    [JsonPropertyName("probesAccepted")]
    public long ProbesAccepted { get; set; }

    [JsonPropertyName("invalidFrames")]
    public long InvalidFrames { get; set; }

    [JsonPropertyName("logSampling")]
    public LogSamplingStatus LogSampling { get; set; } = new();

    [JsonPropertyName("lastMessageAt")]
    public DateTimeOffset? LastMessageAt { get; set; }

    [JsonPropertyName("routes")]
    public Dictionary<string, RouteStatus> Routes { get; set; } = [];
}

public sealed class LogSamplingStatus
{
    [JsonPropertyName("allowedEveryN")]
    public int AllowedEveryN { get; set; } = 1;

    [JsonPropertyName("blockedEveryN")]
    public int BlockedEveryN { get; set; } = 1;

    [JsonPropertyName("errorEveryN")]
    public int ErrorEveryN { get; set; } = 1;
}

public sealed class RouteStatus
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("frameFormat")]
    public string FrameFormat { get; set; } = "Andromeda38";

    [JsonPropertyName("filterEnabled")]
    public bool FilterEnabled { get; set; } = true;

    [JsonPropertyName("activeConnections")]
    public long ActiveConnections { get; set; }

    [JsonPropertyName("totalConnections")]
    public long TotalConnections { get; set; }

    [JsonPropertyName("messages")]
    public long Messages { get; set; }

    [JsonPropertyName("allowed")]
    public long Allowed { get; set; }

    [JsonPropertyName("blocked")]
    public long Blocked { get; set; }

    [JsonPropertyName("wouldBlock")]
    public long WouldBlock { get; set; }
}

public sealed record EventRow(
    DateTimeOffset Time,
    string Category,
    string Action,
    string Port,
    string ObjectCode,
    string RawCode,
    string Event,
    string Remote,
    string Detail,
    double? EvManMilliseconds,
    string FrameHex = "",
    string FrameAscii = "");

public sealed record StatusSample(
    DateTime Time,
    long Allowed,
    long Errors,
    IReadOnlyDictionary<string, long> RouteMessages);
