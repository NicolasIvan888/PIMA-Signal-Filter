[CmdletBinding()]
param(
    [string]$ServiceName = "PimaFilter",
    [string]$StatusFile = "C:\ProgramData\PimaFilter\status.json"
)

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    $service | Format-Table Status,Name,DisplayName -AutoSize
}
else {
    Write-Warning "Serviciul $ServiceName nu este instalat."
}

if (Test-Path -LiteralPath $StatusFile -PathType Leaf) {
    Get-Content -LiteralPath $StatusFile -Raw | ConvertFrom-Json | Format-List
}
else {
    Write-Warning "Fisierul de stare nu exista: $StatusFile"
}

$configFile = "C:\Program Files\PimaFilter\appsettings.json"
if (Test-Path -LiteralPath $configFile) {
    $config = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
    $ports = @($config.PimaFilter.Routes | ForEach-Object { [int]$_.ListenPort })
    Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object LocalPort -in $ports |
        Format-Table LocalAddress,LocalPort,OwningProcess -AutoSize
}
