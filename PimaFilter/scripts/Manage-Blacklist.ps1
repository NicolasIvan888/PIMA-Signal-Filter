[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet("List", "Add", "Enable", "Disable", "Remove")]
    [string]$Operation,
    [ValidatePattern("^[A-Za-z0-9]{6}$")]
    [string]$ObjectCode,
    [string]$Reason = "",
    [string]$Note = "",
    [string]$BlacklistFile = "C:\ProgramData\PimaFilter\config\blacklist.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $BlacklistFile -PathType Leaf)) {
    throw "Blacklist-ul nu exista: $BlacklistFile"
}

$document = Get-Content -LiteralPath $BlacklistFile -Raw | ConvertFrom-Json
$entries = @($document.entries)

if ($Operation -eq "List") {
    $entries |
        Sort-Object objectCode |
        Format-Table objectCode,enabled,reason,note -AutoSize
    return
}

if ([string]::IsNullOrWhiteSpace($ObjectCode)) {
    throw "ObjectCode este obligatoriu pentru operatia $Operation."
}
$ObjectCode = $ObjectCode.ToUpperInvariant()

$existing = $entries | Where-Object objectCode -eq $ObjectCode | Select-Object -First 1
switch ($Operation) {
    "Add" {
        if ($existing) {
            $existing.enabled = $true
            if (-not [string]::IsNullOrWhiteSpace($Reason)) { $existing.reason = $Reason }
            if (-not [string]::IsNullOrWhiteSpace($Note)) { $existing.note = $Note }
        }
        else {
            $entries += [pscustomobject]@{
                objectCode = $ObjectCode
                enabled = $true
                reason = $Reason
                note = $Note
            }
        }
    }
    "Enable" {
        if (-not $existing) { throw "Codul $ObjectCode nu exista in blacklist." }
        $existing.enabled = $true
    }
    "Disable" {
        if (-not $existing) { throw "Codul $ObjectCode nu exista in blacklist." }
        $existing.enabled = $false
    }
    "Remove" {
        if (-not $existing) { throw "Codul $ObjectCode nu exista in blacklist." }
        $entries = @($entries | Where-Object objectCode -ne $ObjectCode)
    }
}

if ($PSCmdlet.ShouldProcess($BlacklistFile, "$Operation $ObjectCode")) {
    $output = [pscustomobject]@{ entries = @($entries) }
    $temporary = $BlacklistFile + ".tmp"
    $output | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $BlacklistFile -Force
    Write-Host "$Operation aplicat pentru $ObjectCode. Serviciul reincarca blacklist-ul automat."
}
