[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceDirectory = "",
    [string]$InstallDirectory = "C:\Program Files\PimaFilter",
    [string]$DataDirectory = "C:\ProgramData\PimaFilter",
    [string]$ServiceName = "PimaFilter",
    [string]$DisplayName = "PIMA Signal Filter",
    [ValidateNotNullOrEmpty()]
    [string]$BindAddress = "127.0.0.1",
    [ValidateNotNullOrEmpty()]
    [string]$UpstreamHost = "",
    [ValidateSet("Monitor", "Block")]
    [string]$Mode = "Monitor",
    [switch]$StartService
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $packagedApp = Join-Path (Split-Path -Parent $PSScriptRoot) "app"
    if (Test-Path -LiteralPath $packagedApp -PathType Container) {
        $SourceDirectory = $packagedApp
    }
    else {
        $SourceDirectory = Join-Path $workspaceRoot "artifacts\PimaFilter-win-x64"
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Porniti PowerShell cu Run as administrator."
}

$sourceExe = Join-Path $SourceDirectory "PimaFilter.Service.exe"
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "Nu exista executabilul publicat: $sourceExe"
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Serviciul '$ServiceName' exista deja. Folositi mai intai scriptul de dezinstalare."
}

if (-not (Get-NetIPAddress -IPAddress $BindAddress -ErrorAction SilentlyContinue)) {
    throw "IP-ul $BindAddress nu este configurat pe acest VM."
}

$sourceConfigPath = Join-Path $SourceDirectory "appsettings.json"
$sourceConfig = Get-Content -LiteralPath $sourceConfigPath -Raw | ConvertFrom-Json
$routes = @($sourceConfig.PimaFilter.Routes)
$routePorts = @($routes | ForEach-Object { [int]$_.ListenPort })
if ($routePorts.Count -eq 0 -or @($routePorts | Sort-Object -Unique).Count -ne $routePorts.Count) {
    throw "Lista porturilor este goala sau contine duplicate."
}
$usedPorts = @(
    Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object LocalPort -in $routePorts
)
if ($usedPorts.Count -gt 0) {
    $ports = ($usedPorts.LocalPort | Sort-Object -Unique) -join ", "
    throw "Porturile sunt deja ocupate pe VM: $ports"
}

if ($PSCmdlet.ShouldProcess($InstallDirectory, "Instalare serviciu $ServiceName")) {
    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $DataDirectory "config") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $DataDirectory "logs") -Force | Out-Null
    Copy-Item -Path (Join-Path $SourceDirectory "*") -Destination $InstallDirectory -Recurse -Force

    $sourceBlacklist = Join-Path $InstallDirectory "data\config\blacklist.json"
    $dataBlacklist = Join-Path $DataDirectory "config\blacklist.json"
    if (-not (Test-Path -LiteralPath $dataBlacklist -PathType Leaf)) {
        Copy-Item -LiteralPath $sourceBlacklist -Destination $dataBlacklist
    }

    $configPath = Join-Path $InstallDirectory "appsettings.json"
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $config.PimaFilter.Mode = $Mode
    $config.PimaFilter.BindAddress = $BindAddress
    $config.PimaFilter.BlacklistFile = $dataBlacklist
    $config.PimaFilter.Diagnostics.LogDirectory = Join-Path $DataDirectory "logs"
    $config.PimaFilter.Diagnostics.StatusFile = Join-Path $DataDirectory "status.json"
    if (-not [string]::IsNullOrWhiteSpace($UpstreamHost)) {
        foreach ($route in $config.PimaFilter.Routes) {
            $route.UpstreamHost = $UpstreamHost
        }
    }
    $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding UTF8

    $installedExe = Join-Path $InstallDirectory "PimaFilter.Service.exe"
    New-Service `
        -Name $ServiceName `
        -BinaryPathName "`"$installedExe`"" `
        -DisplayName $DisplayName `
        -Description "Filtreaza semnalele configurate si retransmite mesajele permise catre receptoarele Andromeda." `
        -StartupType Automatic

    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/30000 | Out-Null
    & sc.exe failureflag $ServiceName 1 | Out-Null

    foreach ($route in $config.PimaFilter.Routes) {
        $allowedSources = @($route.AllowedSourceAddresses | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($allowedSources.Count -eq 0) {
            Write-Warning "Ruta $($route.Name) asculta pe portul $($route.ListenPort), dar nu are regula firewall: AllowedSourceAddresses este gol."
            continue
        }
        New-NetFirewallRule `
            -DisplayName "$DisplayName TCP $($route.ListenPort)" `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $route.ListenPort `
            -RemoteAddress $allowedSources `
            -Program $installedExe `
            -Profile Any | Out-Null
    }

    if ($StartService) {
        Start-Service -Name $ServiceName
    }

    Get-Service -Name $ServiceName
    Write-Host "Mod: $Mode"
    Write-Host "Ascultare: $BindAddress ports $($routePorts -join ', ')"
    foreach ($route in $config.PimaFilter.Routes) {
        Write-Host "$($route.Name): $BindAddress`:$($route.ListenPort) -> $($route.UpstreamHost):$($route.UpstreamPort)"
    }
    Write-Host "Blacklist: $dataBlacklist"
    Write-Host "Status: $(Join-Path $DataDirectory 'status.json')"
    if (-not $StartService) {
        Write-Host "Serviciul a fost instalat, dar nu a fost pornit."
    }
}
