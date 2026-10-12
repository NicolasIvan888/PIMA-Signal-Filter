using PimaFilter.Monitor;

ObjectCodeMapper.Configure(new[]
{
    new ObjectCodeMapper.Target(41001, "Example-Prefix-01", "Andromeda38", "01"),
    new ObjectCodeMapper.Target(41002, "Example-Prefix-02", "Andromeda38", "02"),
    new ObjectCodeMapper.Target(41003, "Example-Prefix-03", "Andromeda38", "03"),
    new ObjectCodeMapper.Target(41004, "Example-Numeric-A", "Andromeda38", "12", 4, 5, 120000),
    new ObjectCodeMapper.Target(41005, "Example-Numeric-B", "Andromeda38", "15", 4, 5, 150000),
    new ObjectCodeMapper.Target(41006, "Example-Atia", "Atia13", "30", 4, 4, 300000),
    new ObjectCodeMapper.Target(41007, "Example-Numeric-C", "Andromeda38", "10", 4, 5, 100000)
});

var root = Path.Combine(Path.GetTempPath(), $"PimaFilterMonitorTests-{Guid.NewGuid():N}");
var logs = Path.Combine(root, "logs");
Directory.CreateDirectory(logs);

try
{
    var configurationPath = Path.Combine(root, "appsettings.json");
    var routeConfiguration = ObjectCodeMapper.Targets.Select(target => new
    {
        Name = target.Name,
        ListenPort = target.Port,
        FrameFormat = target.FrameFormat,
        ObjectPrefix = target.Prefix,
        ObjectCodeBase = target.NumericBase?.ToString() ?? "",
        NumericObjectDigits = target.MaximumInputLength
    }).ToArray();
    await File.WriteAllTextAsync(configurationPath,
        System.Text.Json.JsonSerializer.Serialize(new { PimaFilter = new { Routes = routeConfiguration } }));
    ObjectCodeMapper.Configure(Array.Empty<ObjectCodeMapper.Target>());
    ObjectCodeMapper.LoadConfiguration(configurationPath);
    Assert(ObjectCodeMapper.Targets.Count == 7, "Routes must be read from configuration.");
    Assert(ObjectCodeMapper.ToPort("335096") is null, "Four-digit route must not match five-digit account range.");

    await File.WriteAllTextAsync(
        Path.Combine(root, "status.json"),
        """
        {
          "timestampLocal":"2026-08-18T12:25:06+03:00",
          "startedAt":"2026-08-18T11:58:47+03:00",
          "mode":"Monitor",
          "blacklistCount":1,
          "activeConnections":0,
          "totalConnections":355,
          "allowed":352,
          "blocked":0,
          "errors":3,
          "acksRelayed":352,
          "acksGenerated":0,
          "invalidFrames":3,
          "lastMessageAt":"2026-08-18T12:25:02+03:00",
          "routes":{"41001":{"activeConnections":0,"totalConnections":200,"messages":200,"allowed":197,"blocked":0}}
        }
        """);

    var today = DateTime.Now.ToString("yyyy-MM-dd");
    await File.WriteAllTextAsync(
        Path.Combine(logs, $"allowed-{today}.jsonl"),
        """
        {"timestampLocal":"2026-08-18T12:05:14+03:00","timestampUtc":"2026-08-18T09:05:14Z","details":{"action":"monitor-would-block","route":"Example-Prefix-01","listenPort":41001,"remoteEndpoint":"127.0.0.1:1234","rawObjectCode":"000026","objectCode":"010026","eventField":"5D304","upstreamMilliseconds":31.8}}
        """ + Environment.NewLine);
    await File.WriteAllTextAsync(
        Path.Combine(logs, $"errors-{today}.jsonl"),
        """
        {"timestampLocal":"2026-08-18T12:15:41+03:00","timestampUtc":"2026-08-18T09:15:41Z","details":{"eventType":"upstream-not-acknowledged","details":{"route":"Example-Prefix-01","listenPort":41001,"remoteEndpoint":"127.0.0.1:1517","validationError":"Lungime invalida: 1, asteptat 38.","error":"Timeout EvMan.","elapsedMilliseconds":4001}}}
        """ + Environment.NewLine);
    await File.WriteAllTextAsync(
        Path.Combine(logs, $"blocked-{today}.jsonl"),
        """
        {"timestampLocal":"2026-08-18T15:35:12+03:00","timestampUtc":"2026-08-18T12:35:12Z","details":{"action":"blocked","route":"Example-Prefix-01","listenPort":41001,"remoteEndpoint":"127.0.0.1:4381","rawObjectCode":"005578","objectCode":"015578","eventField":"59304","upstreamMilliseconds":null}}
        """ + Environment.NewLine);
    await File.WriteAllTextAsync(
        Path.Combine(logs, $"invalid-{today}.jsonl"),
        """
        {"timestampLocal":"2026-08-18T15:17:55+03:00","details":{"listenPort":41005,"remoteEndpoint":"127.0.0.1:2247","validationError":"Lungime invalida: 1, asteptat 38.","frameLength":1,"frameHex":"0D","frameAscii":"<CR>"}}
        """ + Environment.NewLine);

    var reader = new MonitorDataReader(root);
    var status = await reader.ReadStatusAsync(CancellationToken.None);
    Assert(status.Mode == "Monitor", "Modul nu a fost citit.");
    Assert(status.Allowed == 352 && status.Errors == 3, "Contoarele nu au fost citite.");
    Assert(status.Routes["41001"].Allowed == 197, "Ruta nu a fost citita.");

    var events = reader.ReadRecentEvents();
    Assert(events.Count == 4, "Nu au fost citite toate jurnalele.");
    Assert(events.Any(item => item.ObjectCode == "010026" && item.Action == "monitor-would-block"), "Obiectul candidat lipseste.");
    Assert(events.Any(item => item.Category == "errors" && item.Detail.Contains("Lungime invalida")), "Eroarea tehnica lipseste.");
    Assert(events.Any(item => item.Category == "invalid" && item.Port == "41005" && item.FrameHex == "0D"),
        "Cadrul invalid cu octetii originali lipseste.");
    var blocked = events.Single(item => item.Category == "blocked");
    Assert(blocked.ObjectCode == "015578" && blocked.EvManMilliseconds is null, "Mesajul blocat cu EvManMs null nu a fost citit corect.");

    Assert(ObjectCodeMapper.BuildFinalCode(41001, "1234") == "011234", "Maparea 41001 este gresita.");
    Assert(ObjectCodeMapper.BuildFinalCode(41002, "1234") == "021234", "Maparea 41002 este gresita.");
    Assert(ObjectCodeMapper.BuildFinalCode(41003, "1234") == "031234", "Maparea 41003 este gresita.");
    Assert(ObjectCodeMapper.BuildFinalCode(41004, "02028") == "122028", "Maparea Example-Numeric-A este gresita.");
    Assert(ObjectCodeMapper.BuildFinalCode(41004, "2028") == "122028", "Example-Numeric-A trebuie sa accepte si codul de patru cifre.");
    Assert(ObjectCodeMapper.BuildFinalCode(41005, "01069") == "151069", "Maparea Example-Numeric-B este gresita.");
    Assert(ObjectCodeMapper.BuildFinalCode(41007, "11256") == "111256", "Maparea Example-Numeric-C este gresita.");
    Assert(ObjectCodeMapper.BuildFinalCode(41007, "1234") == "101234", "ExampleNumeric trebuie sa accepte si codul de patru cifre.");
    Assert(ObjectCodeMapper.BuildFinalCode(41006, "5096") == "305096", "Maparea Example-Atia este gresita.");
    Assert(ObjectCodeMapper.ToPort("021234") == 41002, "Portul nu a fost extras din cod.");
    Assert(ObjectCodeMapper.ToPort("111256") == 41007, "Portul Example-Numeric-C nu a fost extras din cod.");
    Assert(ObjectCodeMapper.ToPort("305096") == 41006, "Portul Example-Atia nu a fost extras din cod.");
    Assert(ObjectCodeMapper.ToPimaCode("305096") == "5096", "Codul Example-Atia nu a fost extras corect.");
    Assert(ObjectCodeMapper.ToPimaCode("031234") == "1234", "Codul PIMA nu a fost extras.");

    var config = Path.Combine(root, "config");
    Directory.CreateDirectory(config);
    var blacklistPath = Path.Combine(config, "blacklist.json");
    await File.WriteAllTextAsync(
        blacklistPath,
        """
        {"entries":[{"objectCode":"010026","enabled":true,"reason":"pilot","note":""}]}
        """);
    var store = new BlacklistStore(root);
    await store.UpsertAsync("021234", "retras", "test", CancellationToken.None);
    var blacklist = await store.LoadAsync(CancellationToken.None);
    Assert(blacklist.Count == 2 && blacklist.Any(entry => entry.ObjectCode == "021234" && entry.Enabled), "Adaugarea blacklist a esuat.");
    Assert(File.Exists(blacklistPath + ".bak"), "Backup-ul blacklist nu a fost creat.");
    await store.SetEnabledAsync("021234", false, CancellationToken.None);
    blacklist = await store.LoadAsync(CancellationToken.None);
    Assert(blacklist.Single(entry => entry.ObjectCode == "021234").Enabled == false, "Dezactivarea a esuat.");
    await store.RemoveAsync("010026", CancellationToken.None);
    blacklist = await store.LoadAsync(CancellationToken.None);
    Assert(blacklist.Count == 1 && blacklist[0].ObjectCode == "021234", "Stergerea a esuat.");

    Console.WriteLine("PASS: testele PIMA Filter Monitor au reusit.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
