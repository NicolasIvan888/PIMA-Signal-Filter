using System.Text.Json;
using System.Text.Json.Serialization;

namespace PimaFilter.Monitor;

public static class ObjectCodeMapper
{
    public sealed record Target(
        int Port,
        string Name,
        string FrameFormat,
        string Prefix,
        int MinimumInputLength = 4,
        int MaximumInputLength = 4,
        int? NumericBase = null);

    private static IReadOnlyDictionary<int, Target> TargetsByPort = new Dictionary<int, Target>();

    public static void Configure(IEnumerable<Target> targets)
    {
        var configured = targets.ToDictionary(target => target.Port);
        foreach (var target in configured.Values)
        {
            if (target.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(target.Name)
                || target.MinimumInputLength is < 1 or > 5
                || target.MaximumInputLength < target.MinimumInputLength || target.MaximumInputLength > 5
                || target.Prefix.Length != 2 || target.Prefix.Any(c => !char.IsAsciiDigit(c))
                || target.FrameFormat is not ("Andromeda38" or "Atia13")
                || target.NumericBase is < 0 or > 900000)
            {
                throw new ArgumentException("Invalid route mapping configuration.");
            }
        }
        TargetsByPort = configured;
    }

    public static void LoadConfiguration(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var targets = new List<Target>();
        foreach (var route in document.RootElement.GetProperty("PimaFilter").GetProperty("Routes").EnumerateArray())
        {
            var format = route.TryGetProperty("FrameFormat", out var formatValue)
                ? formatValue.GetString() ?? "Andromeda38" : "Andromeda38";
            var baseText = route.TryGetProperty("ObjectCodeBase", out var baseValue) ? baseValue.GetString() : null;
            int? numericBase = string.IsNullOrWhiteSpace(baseText) ? null : int.Parse(baseText);
            var digits = numericBase is null ? 4
                : route.TryGetProperty("NumericObjectDigits", out var digitValue) ? digitValue.GetInt32() : 5;
            targets.Add(new Target(route.GetProperty("ListenPort").GetInt32(),
                route.GetProperty("Name").GetString() ?? "Route", format,
                route.GetProperty("ObjectPrefix").GetString() ?? "", Math.Min(4, digits), digits, numericBase));
        }
        Configure(targets);
    }

    public static IReadOnlyList<Target> Targets => TargetsByPort.Values.OrderBy(target => target.Port).ToList();

    public static Target GetTarget(int port) => TargetsByPort.TryGetValue(port, out var target)
        ? target
        : throw new ArgumentOutOfRangeException(nameof(port), "Port PIMA Guard necunoscut.");

    public static string BuildFinalCode(int port, string pimaCode)
    {
        var target = GetTarget(port);

        var normalized = pimaCode.Trim().ToUpperInvariant();
        if (normalized.Length < target.MinimumInputLength || normalized.Length > target.MaximumInputLength
            || (target.NumericBase is not null && normalized.Any(character => !char.IsAsciiDigit(character)))
            || (target.NumericBase is null && normalized.Any(character => !char.IsAsciiLetterOrDigit(character))))
        {
            var format = target.NumericBase is null ? "caractere alfanumerice" : "cifre";
            var length = target.MinimumInputLength == target.MaximumInputLength
                ? $"exact {target.MinimumInputLength}"
                : $"intre {target.MinimumInputLength} si {target.MaximumInputLength}";
            throw new ArgumentException($"Codul obiectului trebuie sa contina {length} {format}.", nameof(pimaCode));
        }

        return target.NumericBase is null
            ? target.Prefix + normalized
            : (target.NumericBase.Value + int.Parse(normalized)).ToString("D6");
    }

    public static string ToPimaCode(string objectCode)
    {
        var normalized = NormalizeAndValidate(objectCode);
        var target = TargetsByPort.Values.FirstOrDefault(item =>
            item.NumericBase is not null
            && int.TryParse(normalized, out var finalNumber)
            && finalNumber >= item.NumericBase
            && finalNumber <= item.NumericBase + (int)Math.Pow(10, item.MaximumInputLength) - 1);
        return target?.NumericBase is int numericBase
            ? (int.Parse(normalized) - numericBase).ToString($"D{target.MaximumInputLength}")
            : normalized[2..];
    }

    public static int? ToPort(string objectCode)
    {
        var normalized = NormalizeAndValidate(objectCode);
        if (int.TryParse(normalized, out var finalNumber))
        {
            var numericTarget = TargetsByPort.Values.FirstOrDefault(target =>
                target.NumericBase is not null
                && finalNumber >= target.NumericBase
                && finalNumber <= target.NumericBase + (int)Math.Pow(10, target.MaximumInputLength) - 1);
            if (numericTarget is not null)
            {
                return numericTarget.Port;
            }
        }

        return TargetsByPort.Values.FirstOrDefault(target =>
            target.NumericBase is null
            && normalized.StartsWith(target.Prefix, StringComparison.OrdinalIgnoreCase))?.Port;
    }

    public static void ValidateFinalCode(string objectCode)
    {
        _ = NormalizeAndValidate(objectCode);
    }

    private static string NormalizeAndValidate(string objectCode)
    {
        var normalized = objectCode.Trim().ToUpperInvariant();
        if (normalized.Length != 6 || normalized.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("Codul final trebuie sa contina exact sase caractere alfanumerice.", nameof(objectCode));
        }

        return normalized;
    }
}

public sealed class BlacklistStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BlacklistStore(string dataDirectory)
    {
        _path = System.IO.Path.Combine(dataDirectory, "config", "blacklist.json");
    }

    public string Path => _path;

    public async Task<IReadOnlyList<BlacklistEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadDocumentUnsafeAsync(cancellationToken)).Entries
                .OrderBy(entry => entry.ObjectCode, StringComparer.Ordinal)
                .Select(entry => entry.Clone())
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(
        string objectCode,
        string reason,
        string note,
        CancellationToken cancellationToken)
    {
        ObjectCodeMapper.ValidateFinalCode(objectCode);
        await MutateAsync(document =>
        {
            var existing = document.Entries.FirstOrDefault(entry =>
                entry.ObjectCode.Equals(objectCode, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                document.Entries.Add(new BlacklistEntry
                {
                    ObjectCode = objectCode,
                    Enabled = true,
                    Reason = reason.Trim(),
                    Note = note.Trim()
                });
            }
            else
            {
                existing.Enabled = true;
                existing.Reason = reason.Trim();
                existing.Note = note.Trim();
            }
        }, cancellationToken);
    }

    public Task SetEnabledAsync(
        string objectCode,
        bool enabled,
        CancellationToken cancellationToken) => MutateAsync(document =>
    {
        var existing = FindRequired(document, objectCode);
        existing.Enabled = enabled;
    }, cancellationToken);

    public Task RemoveAsync(string objectCode, CancellationToken cancellationToken) =>
        MutateAsync(document =>
        {
            var existing = FindRequired(document, objectCode);
            document.Entries.Remove(existing);
        }, cancellationToken);

    private async Task MutateAsync(
        Action<BlacklistDocument> mutation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentUnsafeAsync(cancellationToken);
            mutation(document);
            document.Entries = document.Entries
                .OrderBy(entry => entry.ObjectCode, StringComparer.Ordinal)
                .ToList();
            await SaveDocumentUnsafeAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BlacklistDocument> LoadDocumentUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new BlacklistDocument();
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<BlacklistDocument>(
            stream,
            JsonOptions,
            cancellationToken) ?? new BlacklistDocument();
    }

    private async Task SaveDocumentUnsafeAsync(
        BlacklistDocument document,
        CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(
            directory,
            $"blacklist-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(_path))
            {
                File.Copy(_path, _path + ".bak", overwrite: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static BlacklistEntry FindRequired(BlacklistDocument document, string objectCode) =>
        document.Entries.FirstOrDefault(entry =>
            entry.ObjectCode.Equals(objectCode, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Codul {objectCode} nu exista in blacklist.");
}

public sealed class BlacklistDocument
{
    [JsonPropertyName("entries")]
    public List<BlacklistEntry> Entries { get; set; } = [];
}

public sealed class BlacklistEntry
{
    [JsonPropertyName("objectCode")]
    public string ObjectCode { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    public BlacklistEntry Clone() => new()
    {
        ObjectCode = ObjectCode,
        Enabled = Enabled,
        Reason = Reason,
        Note = Note
    };
}
