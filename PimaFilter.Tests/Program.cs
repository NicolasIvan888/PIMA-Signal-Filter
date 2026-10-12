using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PimaFilter.Configuration;
using PimaFilter.Diagnostics;
using PimaFilter.Protocol;
using PimaFilter.Rules;
using PimaFilter.Services;

var tests = new TestRunner();
await tests.RunAsync();

internal sealed class TestRunner
{
    private const string BlockedFrame = "\n59304______________000026B2_______D5\r";
    private const string AllowedFrame = "\n59304______________000027B2_______D4\r";
    private const string AtiaFrame = "35431----A0\n\r";
    private static readonly string[] CapturedAtiaFrames =
    [
        "32009----OV\n\r",
        "35136----B4\n\r",
        "34254----3w\n\r",
        "36099----SN\n\r",
        "35076----A4\n\r",
        "11229----A4\n\r",
        "34256----3w\n\r",
        "10363----B4\n\r",
        "35600----A4\n\r",
        "34274----3w\n\r",
        AtiaFrame
    ];

    public async Task RunAsync()
    {
        ParserDecodesCapturedMessage();
        ParserMapsAllThreePrefixRoutes();
        ParserRejectsInvalidLength();
        ParserDecodesCapturedRemoteFormats();
        ParserAddsNumericBaseForConfiguredObjectDigits();
        ParserAcceptsAlphanumericCodesAndProbes();
        await FrameReaderReassemblesSplitMessagesAsync();
        await AuditSamplingPreservesBoundedDetailAsync();
        await ProxyMonitorAndBlockIntegrationAsync();
        await ProxyRelaysUnverifiedRoutesInBlockModeAsync();
        await ProxyHandlesConcurrentConnectionsAsync();
        Console.WriteLine("PASS: toate testele PIMA Filter au reusit.");
    }

    private static async Task AuditSamplingPreservesBoundedDetailAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "signal-filter-audit-" + Guid.NewGuid().ToString("N"));
        var options = new AppOptions
        {
            Diagnostics = new DiagnosticsOptions
            {
                LogDirectory = root,
                AllowedLogEveryN = 3,
                BlockedLogEveryN = 4,
                ErrorLogEveryN = 5
            }
        };
        var audit = new DailyAuditLog(Options.Create(options));
        for (var index = 0; index < 10; index++)
        {
            await audit.WriteAllowedAsync(new { index }, CancellationToken.None);
            await audit.WriteBlockedAsync(new { index }, CancellationToken.None);
            await audit.WriteErrorAsync("same-error", new { index }, CancellationToken.None);
            await audit.WriteInvalidAsync(new { frameHex = "0D", frameLength = 1, index }, CancellationToken.None);
        }

        Assert(File.ReadLines(Directory.GetFiles(root, "allowed-*.jsonl").Single()).Count() == 4,
            "Jurnalul permis trebuie esantionat 1/3 plus primul eveniment.");
        Assert(File.ReadLines(Directory.GetFiles(root, "blocked-*.jsonl").Single()).Count() == 3,
            "Jurnalul blocat trebuie esantionat 1/4 plus primul eveniment.");
        Assert(File.ReadLines(Directory.GetFiles(root, "errors-*.jsonl").Single()).Count() == 3,
            "Jurnalul de erori trebuie esantionat 1/5 plus primul eveniment.");
        var invalidLines = File.ReadLines(Directory.GetFiles(root, "invalid-*.jsonl").Single()).ToArray();
        Assert(invalidLines.Length == 10 && invalidLines.All(line => line.Contains("\"frameHex\":\"0D\"")),
            "Fiecare cadru invalid trebuie jurnalizat cu octetii primiti.");
        Directory.Delete(root, recursive: true);
    }

    private static async Task ProxyHandlesConcurrentConnectionsAsync()
    {
        const int messageCount = 100;
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "pima-filter-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var proxyPort = GetFreePort();
        var upstreamPort = GetFreePort();
        while (upstreamPort == proxyPort)
        {
            upstreamPort = GetFreePort();
        }

        var options = new AppOptions
        {
            Mode = "Monitor",
            BindAddress = "127.0.0.1",
            MaxConcurrentConnections = 128,
            BlacklistFile = Path.Combine(temporaryRoot, "blacklist.json"),
            Routes =
            [
                new RouteOptions
                {
                    Name = "load-route",
                    ListenPort = proxyPort,
                    UpstreamHost = "127.0.0.1",
                    UpstreamPort = upstreamPort,
                    ObjectPrefix = "01"
                }
            ],
            Diagnostics = new DiagnosticsOptions
            {
                LogDirectory = Path.Combine(temporaryRoot, "logs"),
                StatusFile = Path.Combine(temporaryRoot, "status.json")
            }
        };

        await File.WriteAllTextAsync(options.BlacklistFile, "{\"entries\":[]}");
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        var audit = new DailyAuditLog(Options.Create(options));
        var blacklist = new BlacklistProvider(Options.Create(options), audit);
        await blacklist.ReloadAsync(CancellationToken.None);
        var worker = new PimaProxyWorker(
            new MutableOptionsMonitor<AppOptions>(options),
            blacklist,
            audit,
            new HealthState(),
            loggerFactory.CreateLogger<PimaProxyWorker>());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var upstream = RunConcurrentMockEvManAsync(upstreamPort, messageCount, timeout.Token);
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100, timeout.Token);

        var acknowledgements = await Task.WhenAll(
            Enumerable.Range(0, messageCount).Select(_ => SendFrameAsync(proxyPort, AllowedFrame)));

        Assert(acknowledgements.All(value => value == 0x06), "Nu toate conexiunile concurente au primit ACK.");
        Assert(await upstream == messageCount, "EvMan fals nu a primit toate mesajele concurente.");
        await worker.StopAsync(CancellationToken.None);
        Directory.Delete(temporaryRoot, recursive: true);
    }

    private static void ParserMapsAllThreePrefixRoutes()
    {
        var portTwo = PimaMessage.Parse(
            Encoding.ASCII.GetBytes("\n59303______________004516B4_______CC\r"),
            "02");
        var portThree = PimaMessage.Parse(
            Encoding.ASCII.GetBytes("\n116120235328_______00314100_______D4\r"),
            "03");

        Assert(portTwo.ObjectCode == "024516", "Maparea portului 41002 este gresita.");
        Assert(portThree.ObjectCode == "033141", "Maparea portului 41003 este gresita.");
    }

    private static void ParserDecodesCapturedMessage()
    {
        var frame = Encoding.ASCII.GetBytes(BlockedFrame);
        var message = PimaMessage.Parse(frame, "01");

        Assert(message.IsValid, "Mesajul capturat trebuie sa fie valid.");
        Assert(message.RawObjectCode == "000026", "Cod brut incorect.");
        Assert(message.ObjectCode == "010026", "Cod Andromeda incorect.");
        Assert(message.EventField == "59304", "Camp eveniment incorect.");
        Assert(message.Qualifier == "B2", "Calificator incorect.");
        Assert(message.Checksum == "D5", "Checksum extras incorect.");
    }

    private static void ParserAddsNumericBaseForConfiguredObjectDigits()
    {
        var pupitru = PimaMessage.Parse(
            Encoding.ASCII.GetBytes("\n59102______________011256SF0______B4\r"),
            "10",
            "Andromeda38",
            "100000");
        var atiaMessage = PimaMessage.Parse(
            Encoding.ASCII.GetBytes("35096----SN\n\r"),
            "30",
            "Atia13",
            "300000",
            4);

        Assert(pupitru.RawObjectCode == "011256" && pupitru.ObjectCode == "111256",
            "ExampleNumeric trebuie sa adauge 100000 la codul de cinci cifre 11256.");
        Assert(atiaMessage.RawObjectCode == "35096" && atiaMessage.ObjectCode == "305096",
            "Example-Atia trebuie sa ignore prima cifra si sa adauge 300000 la codul 5096.");
    }

    private static void ParserRejectsInvalidLength()
    {
        var message = PimaMessage.Parse(Encoding.ASCII.GetBytes("\n123\r"), "01");
        Assert(!message.IsValid, "Mesajul scurt trebuie marcat invalid.");
    }

    private static void ParserDecodesCapturedRemoteFormats()
    {
        var numericMessage = PimaMessage.Parse(
            Encoding.ASCII.GetBytes("\n116080335328_______00047300_______C9\r"), "");
        Assert(numericMessage.IsValid && numericMessage.RawObjectCode == "000473" && numericMessage.ObjectCode is null,
            "Example-Numeric-A trebuie citit fara a presupune codul obiectului Andromeda.");

        foreach (var frame in CapturedAtiaFrames)
        {
            var atiaMessage = PimaMessage.Parse(Encoding.ASCII.GetBytes(frame), "", "Atia13");
            Assert(atiaMessage.IsValid && atiaMessage.RawObjectCode == frame[..5].ToUpperInvariant()
                && atiaMessage.EventField == frame.Substring(9, 2) && atiaMessage.ObjectCode is null,
                "Un cadru Atia13 din captura Example-Atia nu a fost citit corect.");
        }
    }

    private static void ParserAcceptsAlphanumericCodesAndProbes()
    {
        var andromeda = PimaMessage.Parse(
            Encoding.ASCII.GetBytes("\n55304______________01A01001_______E1\r"), "12");
        Assert(andromeda.IsValid && andromeda.RawObjectCode == "01A010" && andromeda.ObjectCode == "12A010",
            "Codurile Andromeda alfanumerice trebuie sa poata fi filtrate.");

        var atia = PimaMessage.Parse(Encoding.ASCII.GetBytes("3E005----01\n\r"), "30", "Atia13");
        Assert(atia.IsValid && atia.RawObjectCode == "3E005" && atia.ObjectCode == "30E005",
            "Identificatorul Atia trebuie mapat la codul final de sase caractere.");

        var probe = PimaMessage.Parse(Encoding.ASCII.GetBytes("?"), "01");
        Assert(probe.IsValid && probe.IsCommunicationProbe, "Verificarea PIMA Guard trebuie recunoscuta.");
    }

    private static async Task ProxyRelaysUnverifiedRoutesInBlockModeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pima-unverified-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstPort = GetFreePort();
        var secondPort = GetFreePort();
        var upstreamPort = GetFreePort();
        while (new[] { firstPort, secondPort, upstreamPort }.Distinct().Count() != 3)
        {
            secondPort = GetFreePort();
            upstreamPort = GetFreePort();
        }

        var options = new AppOptions
        {
            Mode = "Block",
            BindAddress = "127.0.0.1",
            BlacklistFile = Path.Combine(root, "blacklist.json"),
            Routes =
            [
                new RouteOptions { Name = "unverified-38", ListenPort = firstPort,
                    UpstreamHost = "127.0.0.1", UpstreamPort = upstreamPort,
                    ObjectPrefix = "01", FilterEnabled = false },
                new RouteOptions { Name = "atia-13", ListenPort = secondPort,
                    UpstreamHost = "127.0.0.1", UpstreamPort = upstreamPort,
                    FrameFormat = "Atia13", FilterEnabled = false }
            ],
            Diagnostics = new DiagnosticsOptions
            {
                LogDirectory = Path.Combine(root, "logs"),
                StatusFile = Path.Combine(root, "status.json")
            }
        };
        await File.WriteAllTextAsync(options.BlacklistFile,
            "{\"entries\":[{\"objectCode\":\"010026\",\"enabled\":true,\"reason\":\"test\"}]}");
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        var audit = new DailyAuditLog(Options.Create(options));
        var blacklist = new BlacklistProvider(Options.Create(options), audit);
        await blacklist.ReloadAsync(CancellationToken.None);
        var health = new HealthState();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<string>();
        var upstream = RunVariableLengthMockAsync(upstreamPort, received, 3, timeout.Token);
        var worker = new PimaProxyWorker(new MutableOptionsMonitor<AppOptions>(options),
            blacklist, audit, health, loggerFactory.CreateLogger<PimaProxyWorker>());
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100, timeout.Token);
        Assert(await SendFrameAsync(firstPort, BlockedFrame) == 0x06,
            "Ruta de 38 octeti nevalidata trebuie sa transmita ACK-ul din amonte.");
        Assert(await SendFrameAsync(secondPort, AtiaFrame) == 0x06,
            "Ruta Atia13 trebuie sa transmita ACK-ul din amonte.");
        Assert(await SendFrameAsync(firstPort, "\r") == 0x06,
            "Un cadru de un octet confirmat de amonte trebuie tratat ca in versiunea existenta.");
        await upstream;
        await worker.StopAsync(CancellationToken.None);
        Assert(received.Contains(BlockedFrame) && received.Contains(AtiaFrame),
            "Modul Block a oprit un cadru de pe o ruta nevalidata.");
        var invalidAudit = File.ReadLines(Directory.GetFiles(options.Diagnostics.LogDirectory, "invalid-*.jsonl").Single()).Single();
        Assert(invalidAudit.Contains("\"frameHex\":\"0D\""),
            "Cadrul de un octet nu a fost pastrat in jurnalul de cadre invalide.");
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(
            health.Snapshot(options.Mode, 1, options.Diagnostics)));
        Assert(snapshot.RootElement.GetProperty("blocked").GetInt64() == 0
            && snapshot.RootElement.GetProperty("acksRelayed").GetInt64() == 3,
            "Contoarele sau provenienta ACK-urilor sunt gresite.");
        Directory.Delete(root, recursive: true);
    }

    private static async Task RunVariableLengthMockAsync(int port, List<string> frames,
        int count, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            for (var index = 0; index < count; index++)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                using var stream = client.GetStream();
                var bytes = new List<byte>();
                var one = new byte[1];
                do
                {
                    await stream.ReadExactlyAsync(one, cancellationToken);
                    bytes.Add(one[0]);
                } while (one[0] != 0x0D);
                frames.Add(Encoding.ASCII.GetString(bytes.ToArray()));
                await stream.WriteAsync(new byte[] { 0x06 }, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task FrameReaderReassemblesSplitMessagesAsync()
    {
        var bytes = Encoding.ASCII.GetBytes(BlockedFrame + AllowedFrame);
        await using var stream = new ChunkedReadStream(bytes, 7);
        var reader = new PimaFrameReader();

        var first = await reader.ReadFrameAsync(stream, 512, CancellationToken.None);
        var second = await reader.ReadFrameAsync(stream, 512, CancellationToken.None);
        var end = await reader.ReadFrameAsync(stream, 512, CancellationToken.None);

        Assert(Encoding.ASCII.GetString(first!) == BlockedFrame, "Primul frame reasamblat este gresit.");
        Assert(Encoding.ASCII.GetString(second!) == AllowedFrame, "Al doilea frame reasamblat este gresit.");
        Assert(end is null, "Fluxul trebuie sa fie epuizat.");
    }

    private static async Task ProxyMonitorAndBlockIntegrationAsync()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "pima-filter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        var proxyPort = GetFreePort();
        var upstreamPort = GetFreePort();
        while (upstreamPort == proxyPort)
        {
            upstreamPort = GetFreePort();
        }

        var blacklistPath = Path.Combine(temporaryRoot, "blacklist.json");
        await File.WriteAllTextAsync(
            blacklistPath,
            JsonSerializer.Serialize(new
            {
                entries = new[]
                {
                    new
                    {
                        objectCode = "010026",
                        enabled = true,
                        reason = "integration-test",
                        note = ""
                    }
                }
            }));

        var options = new AppOptions
        {
            Mode = "Monitor",
            BindAddress = "127.0.0.1",
            BlacklistFile = blacklistPath,
            Routes =
            [
                new RouteOptions
                {
                    Name = "test-route",
                    ListenPort = proxyPort,
                    UpstreamHost = "127.0.0.1",
                    UpstreamPort = upstreamPort,
                    ObjectPrefix = "01"
                }
            ],
            Diagnostics = new DiagnosticsOptions
            {
                LogDirectory = Path.Combine(temporaryRoot, "logs"),
                StatusFile = Path.Combine(temporaryRoot, "status.json"),
                AllowedRetentionDays = 1,
                BlockedRetentionDays = 7,
                ErrorRetentionDays = 30
            }
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        var optionsMonitor = new MutableOptionsMonitor<AppOptions>(options);
        var audit = new DailyAuditLog(Options.Create(options));
        var health = new HealthState();
        var blacklist = new BlacklistProvider(Options.Create(options), audit);
        await blacklist.ReloadAsync(CancellationToken.None);

        var receivedFrames = new List<string>();
        using var mockStop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var mockTask = RunMockEvManAsync(upstreamPort, receivedFrames, expectedConnections: 2, mockStop.Token);

        var worker = new PimaProxyWorker(
            optionsMonitor,
            blacklist,
            audit,
            health,
            loggerFactory.CreateLogger<PimaProxyWorker>());

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        var monitorAck = await SendFrameAsync(proxyPort, BlockedFrame);
        Assert(monitorAck == 0x06, "MONITOR trebuie sa retransmita ACK 06.");

        options.Mode = "Block";
        var blockedAck = await SendFrameAsync(proxyPort, BlockedFrame);
        Assert(blockedAck == 0x06, "BLOCK trebuie sa genereze local ACK 06.");

        var allowedAck = await SendFrameAsync(proxyPort, AllowedFrame);
        Assert(allowedAck == 0x06, "Codul absent din blacklist trebuie transmis si confirmat.");

        await mockTask;
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        Assert(receivedFrames.Count == 2, "EvMan fals trebuie sa primeasca exact doua mesaje.");
        Assert(receivedFrames[0] == BlockedFrame, "MONITOR nu trebuie sa elimine mesajul.");
        Assert(receivedFrames[1] == AllowedFrame, "Mesajul permis nu a ajuns la EvMan fals.");

        using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(
            health.Snapshot(options.Mode, 1, options.Diagnostics))))
        {
            Assert(snapshot.RootElement.GetProperty("wouldBlock").GetInt64() == 1,
                "Contorul exact pentru Monitor ar fi blocat este gresit.");
            Assert(snapshot.RootElement.GetProperty("blocked").GetInt64() == 1,
                "Contorul exact al mesajelor blocate este gresit.");
        }

        var allowedLog = Directory.GetFiles(options.Diagnostics.LogDirectory, "allowed-*.jsonl").Single();
        var blockedLog = Directory.GetFiles(options.Diagnostics.LogDirectory, "blocked-*.jsonl").Single();
        var allowedText = await File.ReadAllTextAsync(allowedLog);
        var blockedText = await File.ReadAllTextAsync(blockedLog);
        Assert(allowedText.Contains("monitor-would-block"), "Lipseste decizia MONITOR din jurnal.");
        Assert(blockedText.Contains("010026"), "Lipseste codul blocat din jurnal.");

        Directory.Delete(temporaryRoot, recursive: true);
    }

    private static async Task RunMockEvManAsync(
        int port,
        List<string> receivedFrames,
        int expectedConnections,
        CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            for (var index = 0; index < expectedConnections; index++)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                using var stream = client.GetStream();
                var frame = new byte[38];
                await stream.ReadExactlyAsync(frame, cancellationToken);
                lock (receivedFrames)
                {
                    receivedFrames.Add(Encoding.ASCII.GetString(frame));
                }

                await stream.WriteAsync(new byte[] { 0x06 }, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<int> RunConcurrentMockEvManAsync(
        int port,
        int expectedConnections,
        CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var handlers = new List<Task>();
        try
        {
            for (var index = 0; index < expectedConnections; index++)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                handlers.Add(Task.Run(async () =>
                {
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        var frame = new byte[38];
                        await stream.ReadExactlyAsync(frame, cancellationToken);
                        await stream.WriteAsync(new byte[] { 0x06 }, cancellationToken);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(handlers);
            return handlers.Count;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<byte> SendFrameAsync(int port, string frame)
    {
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(frame));
        var response = new byte[1];
        await stream.ReadExactlyAsync(response);
        return response[0];
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("TEST FAILED: " + message);
        }
    }
}

internal sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

internal sealed class ChunkedReadStream(byte[] content, int chunkSize) : Stream
{
    private int _offset;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => content.Length;
    public override long Position { get => _offset; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_offset >= content.Length)
        {
            return 0;
        }

        var count = Math.Min(Math.Min(chunkSize, buffer.Length), content.Length - _offset);
        content.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
