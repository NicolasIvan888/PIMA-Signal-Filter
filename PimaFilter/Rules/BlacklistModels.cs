namespace PimaFilter.Rules;

public sealed class BlacklistDocument
{
    public List<BlacklistEntry> Entries { get; set; } = [];
}

public sealed class BlacklistEntry
{
    public string ObjectCode { get; set; } = "";
    public bool Enabled { get; set; }
    public string Reason { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed record BlacklistRule(
    string ObjectCode,
    string Reason,
    string Note);
