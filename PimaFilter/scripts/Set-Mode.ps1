[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Monitor", "Block")]
    [string]$Mode,
    [string]$ConfigFile = "C:\Program Files\PimaFilter\appsettings.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ConfigFile -PathType Leaf)) {
    throw "Configuratia nu exista: $ConfigFile"
}

if ($PSCmdlet.ShouldProcess($ConfigFile, "Schimbare mod in $Mode")) {
    $config = Get-Content -LiteralPath $ConfigFile -Raw | ConvertFrom-Json
    $config.PimaFilter.Mode = $Mode
    $temporary = $ConfigFile + ".tmp"
    $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $ConfigFile -Force
    Write-Host "Modul a fost schimbat in $Mode. Serviciul reincarca automat configuratia."
}
