# PIMA Signal Filter

Windows TCP relay and configurable account blacklist for PIMA Guard / Andromeda alarm frames, with a Windows Forms operational monitor.

## Features

- Multiple configurable TCP routes with independent destinations and account mapping.
- Andromeda38 and Atia13 frame parsing; original permitted frames are relayed unchanged.
- Monitor mode records which accounts would be blocked. Block mode acknowledges a matched frame locally without relaying it.
- File-based blacklist with automatic reload, enable/disable, notes and CSV export.
- Numeric account-base mapping or two-character prefix mapping.
- Diagnostic JSONL logs, configurable sampling and retention, route counters and ACK statistics.
- A desktop monitor with recent messages, frame details, a blacklist editor and stable scrolling.

This repository contains generic source code and empty data. It does not contain an operational network configuration, customer blacklist, production logs or installation packages from a private deployment.

## Requirements and build

Windows and the .NET 10 SDK are required for the desktop monitor. The service can also run in console mode for local development.

From the repository root:

```powershell
dotnet restore PIMA-Signal-Filter.slnx --configfile NuGet.Config
dotnet build PIMA-Signal-Filter.slnx --configuration Release --no-restore
dotnet run --project PimaFilter.Tests --configuration Release --no-build
dotnet run --project PimaFilter.Monitor.Tests --configuration Release --no-build
```

The tests use loopback addresses and synthetic route identifiers. They do not connect to a production receiver.

## Configure your own installation

`PimaFilter/appsettings.json` ships in Monitor mode with an empty route list and an empty blacklist. It intentionally cannot start receiving signals until routes are configured.

Use `examples/appsettings.routes.template.json` as a template. Replace zero-valued ports, the upstream hostname and route/account settings with your own values. All configured listening ports must be distinct and between 1 and 65535. Select an address assigned to the host, or bind to loopback for local tests.

Each route defines:

| Setting | Purpose |
|---|---|
| Name | Display label |
| ListenPort | TCP port selected by the operator |
| UpstreamHost / UpstreamPort | Receiver destination selected by the operator |
| FrameFormat | Andromeda38 or Atia13 |
| FilterEnabled | Whether the route compares accounts with the blacklist |
| ObjectPrefix | Two numeric characters for prefix mapping |
| ObjectCodeBase | Optional six-digit numeric base |
| NumericObjectDigits | Number of significant trailing account digits, from 1 to 5 |
| AllowedSourceAddresses | Optional list of exact source IP addresses allowed by the service |

The monitor reads route mappings from the same service configuration; route ports are not hardcoded into the UI. Restart the monitor after changing route mappings.

### Account mapping examples

- Prefix mapping: prefix `01` and object `1234` produce final code `011234`.
- Numeric mapping with five significant digits: base `200000` and object `11256` produce `211256`.
- Atia13 with four significant digits: raw field `34254`, base `400000` and digits `4` produce `404254`; the first raw digit is excluded from comparison.

These transformations affect blacklist comparison and display only. They do not rewrite a permitted frame sent to the upstream receiver. Choose disjoint account ranges if you need unambiguous reverse mapping in the monitor. Numeric-base mapping does not compare alphanumeric accounts; they are relayed without a numeric blacklist match.

## Run and install

For local development after configuration:

```powershell
dotnet run --project PimaFilter
```

Publish self-contained Windows executables using the included scripts:

```powershell
.\PimaFilter\scripts\Package-Deployment.ps1 -Version 0.5.1
.\PimaFilter.Monitor\scripts\Package-Monitor.ps1 -Version 0.5.1
```

Configure the published service's `appsettings.json` before installation. In an administrator PowerShell window:

```powershell
.\PimaFilter\scripts\Install-Service.ps1 -SourceDirectory '.\artifacts\PimaFilter-win-x64-0.5.1' -BindAddress 'YOUR_LOCAL_BIND_ADDRESS'
.\PimaFilter.Monitor\scripts\Install-Monitor.ps1 -SourceDirectory '.\artifacts\PimaFilter-Monitor-win-x64-0.5.1\' -StartMonitor
```

The service installation defaults to Monitor mode and does not start automatically during installation unless `-StartService` is supplied. Configure permitted source addresses before exposing a listening port. The installation script adds firewall allow rules only for routes with source restrictions.

Installed monitor defaults:

- Configuration: `C:\Program Files\PimaFilter\appsettings.json`
- Runtime data: `C:\ProgramData\PimaFilter`

Override both paths for a development installation:

```powershell
.\PimaFilter.Monitor.exe --config="PATH_TO_SERVICE_CONFIG" --data="PATH_TO_RUNTIME_DATA"
```

## Operational checks

```powershell
Get-Service PimaFilter
Get-CimInstance Win32_Service -Filter "Name='PimaFilter'" | Format-List State, ProcessId, PathName
.\PimaFilter\scripts\Get-Status.ps1
```

Restart when necessary:

```powershell
Restart-Service PimaFilter
```

Restarting resets in-memory counters and briefly interrupts active connections. Blacklist edits reload automatically without a service restart.

## Behavior and limitations

- TCP only; UDP is not supported by this filter.
- No alarm is generated merely because a channel has been idle. An inactivity watchdog is not implemented.
- Permitted messages depend on the upstream receiver's ACK. Timeouts and non-ACK replies are logged.
- Unrecognized or invalid frames are logged and still relayed; this preserves compatibility but is not a protocol firewall.
- If a blacklist reload fails, active rules are cleared and traffic is relayed. Check errors and the loaded blacklist count.
- Log sampling can omit individual permitted and blocked messages. Counters and log row counts therefore need not match.
- A routing or NAT path that bypasses this service cannot be filtered by it.
- Validate receiver ACK behavior, account mapping and routing in a controlled environment before enabling Block mode.

## License

MIT, copyright 2026 Ivanov Nicolae. See [LICENSE](LICENSE).
