[CmdletBinding()]
param(
    [string]$Version = "0.5.1",
    [string]$Runtime = "win-x64",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$artifactDirectory = Join-Path $workspaceRoot "artifacts\PimaFilter-$Runtime-$Version"
$deploymentRoot = Join-Path $workspaceRoot "deployment\PimaFilter-$Version"
$zipPath = Join-Path $workspaceRoot "deployment\PimaFilter-$Version.zip"
$workspaceFull = [IO.Path]::GetFullPath($workspaceRoot).TrimEnd('\') + '\'
$deploymentFull = [IO.Path]::GetFullPath($deploymentRoot)
if (-not $deploymentFull.StartsWith($workspaceFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Directorul de deployment trebuie sa ramana in workspace: $deploymentFull"
}

& (Join-Path $PSScriptRoot "Publish-Windows.ps1") `
    -Runtime $Runtime `
    -OutputDirectory $artifactDirectory `
    -NoRestore:$NoRestore

if (Test-Path -LiteralPath $deploymentRoot -PathType Container) {
    Remove-Item -LiteralPath $deploymentRoot -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $deploymentRoot "app") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $deploymentRoot "scripts") -Force | Out-Null

Copy-Item -Path (Join-Path $artifactDirectory "*") -Destination (Join-Path $deploymentRoot "app") -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "*.ps1") -Destination (Join-Path $deploymentRoot "scripts") -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "DEPLOYMENT.txt") -Destination (Join-Path $deploymentRoot "INSTRUCTIUNE.txt")

if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -LiteralPath $deploymentRoot -DestinationPath $zipPath -CompressionLevel Optimal

$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
$hashPath = $zipPath + ".sha256.txt"
"$($hash.Hash)  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Host "Pachet deployment: $zipPath"
Write-Host "SHA256: $hashPath"
