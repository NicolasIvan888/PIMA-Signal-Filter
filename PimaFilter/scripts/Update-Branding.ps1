[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PackageDirectory = '',
    [string]$InstallDirectory = 'C:\Program Files\PimaFilter',
    [string]$ServiceName = 'PimaFilter'
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { $PackageDirectory = Split-Path -Parent $PSScriptRoot }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Porniti PowerShell ca Administrator.'
}
$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
if (-not $service) { throw "Serviciul $ServiceName nu exista." }
$target = Join-Path $InstallDirectory 'PimaFilter.Service.exe'
$source = Join-Path $PackageDirectory 'app\PimaFilter.Service.exe'
foreach ($path in @($target,$source)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Fisier lipsa: $path" }
}
if ($service.PathName -notmatch [regex]::Escape($target)) { throw 'Serviciul ruleaza din alta cale.' }
$backup = Join-Path (Split-Path -Parent $PackageDirectory) ('PimaFilter-branding-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (-not $PSCmdlet.ShouldProcess($target, 'Actualizare denumire PIMA Signal Filter')) { return }
New-Item -ItemType Directory -Path $backup -ErrorAction Stop | Out-Null
Copy-Item -LiteralPath $target -Destination (Join-Path $backup 'PimaFilter.Service.exe')
$wasRunning = $service.State -eq 'Running'
$oldDisplayName = $service.DisplayName
function Stop-PimaProcess {
    $currentService = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    $process = if ($currentService.ProcessId -gt 0) { Get-Process -Id $currentService.ProcessId -ErrorAction SilentlyContinue } else { $null }
    Stop-Service -Name $ServiceName -ErrorAction Stop
    (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    if ($process -and -not $process.HasExited -and -not $process.WaitForExit(30000)) {
        throw 'Procesul serviciului nu s-a inchis. Actualizarea este oprita.'
    }
}
try {
    Stop-PimaProcess
    Copy-Item -LiteralPath $source -Destination $target -Force
    & sc.exe config $ServiceName DisplayName= 'PIMA Signal Filter' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Nu s-a putut schimba denumirea serviciului.' }
    if ($wasRunning) {
        Start-Service -Name $ServiceName -ErrorAction Stop
        (Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Start-Sleep -Seconds 2
        if ((Get-Service -Name $ServiceName).Status -ne 'Running') { throw 'Serviciul nu a ramas pornit.' }
    }
}
catch {
    $failure = $_
    Stop-PimaProcess
    Copy-Item -LiteralPath (Join-Path $backup 'PimaFilter.Service.exe') -Destination $target -Force
    & sc.exe config $ServiceName DisplayName= $oldDisplayName | Out-Null
    if ($wasRunning) { Start-Service -Name $ServiceName -ErrorAction Stop }
    throw "Actualizarea a esuat; executabilul anterior a fost restaurat. Cauza: $failure"
}
Write-Host "Actualizare reusita. Backup: $backup"
Write-Host 'Configuratia, rutele si blacklist-ul existent au fost pastrate.'
Get-Service -Name $ServiceName | Format-Table Status,Name,DisplayName -AutoSize
