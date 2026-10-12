using System.Text;
using System.Text.Json;

namespace PimaFilter.Monitor;

public sealed class MonitorDataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataDirectory;
    private readonly string _statusPath;
    private readonly string _logDirectory;

    public MonitorDataReader(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        _statusPath = Path.Combine(dataDirectory, "status.json");
        _logDirectory = Path.Combine(dataDirectory, "logs");
    }

    public string DataDirectory => _dataDirectory;
    public string LogDirectory => _logDirectory;

    public async Task<FilterStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    _statusPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var status = await JsonSerializer.DeserializeAsync<FilterStatus>(
                    stream,
                    JsonOptions,
                    cancellationToken);
                return status ?? throw new InvalidDataException("status.json este gol.");
            }
            catch (Exception exception) when (
                exception is IOException or JsonException && attempt < 2)
            {
                lastError = exception;
                await Task.Delay(80, cancellationToken);
            }
        }

        throw new IOException(
            $"Nu pot citi {_statusPath}: {lastError?.Message}",
            lastError);
    }

    public IReadOnlyList<EventRow> ReadRecentEvents(int maximumRows = 250)
    {
        var rows = new List<EventRow>();
        foreach (var category in new[] { "allowed", "blocked", "errors", "invalid" })
        {
            var path = Path.Combine(_logDirectory, $"{category}-{DateTime.Now:yyyy-MM-dd}.jsonl");
            foreach (var line in ReadTailLines(path, 100, 256 * 1024))
            {
                if (TryParseEvent(line, category, out var row))
                {
                    rows.Add(row);
                }
            }
        }

        return rows
            .OrderByDescending(row => row.Time)
            .Take(maximumRows)
            .ToArray();
    }

    private static bool TryParseEvent(string line, string category, out EventRow row)
    {
        row = default!;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var time = ReadDate(root, "timestampLocal") ?? DateTimeOffset.MinValue;
            var outer = root.GetProperty("details");
            var eventType = category == "errors" ? ReadString(outer, "eventType") : string.Empty;
            var details = category == "errors" && outer.TryGetProperty("details", out var nested)
                ? nested
                : outer;

            var validation = ReadString(details, "validationError");
            var error = ReadString(details, "error");
            var detail = string.Join(
                " | ",
                new[] { eventType, validation, error }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            row = new EventRow(
                time,
                category,
                ReadString(details, "action", category),
                ReadNumberText(details, "listenPort"),
                ReadString(details, "objectCode"),
                ReadString(details, "rawObjectCode"),
                ReadString(details, "eventField"),
                ReadString(details, "remoteEndpoint"),
                detail,
                ReadDouble(details, "upstreamMilliseconds") ?? ReadDouble(details, "elapsedMilliseconds"),
                ReadString(details, "frameHex"),
                ReadString(details, "frameAscii"));
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static IEnumerable<string> ReadTailLines(
        string path,
        int maximumLines,
        int maximumBytes)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bytesToRead = (int)Math.Min(stream.Length, maximumBytes);
            stream.Seek(-bytesToRead, SeekOrigin.End);
            var buffer = new byte[bytesToRead];
            var read = stream.Read(buffer, 0, buffer.Length);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var start = stream.Length > bytesToRead && lines.Length > 0 ? 1 : 0;
            return lines
                .Skip(start)
                .TakeLast(maximumLines)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static string ReadString(JsonElement element, string name, string fallback = "") =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value.ToString()
            : fallback;

    private static string ReadNumberText(JsonElement element, string name) =>
        ReadString(element, name);

    private static double? ReadDouble(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
            ? number
            : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.TryGetDateTimeOffset(out var date)
            ? date
            : null;
}
