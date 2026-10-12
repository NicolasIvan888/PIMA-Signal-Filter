[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = "",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$projectPath = Join-Path $projectRoot "PimaFilter.csproj"
$testsPath = Join-Path $workspaceRoot "PimaFilter.Tests\PimaFilter.Tests.csproj"
$nugetConfig = Join-Path $workspaceRoot "NuGet.Config"
$dotnetLocal = Join-Path $workspaceRoot ".dotnet-sdk\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $dotnetLocal -PathType Leaf) {
    $dotnetLocal
}
else {
    "dotnet"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $workspaceRoot "artifacts\PimaFilter-$Runtime"
}

if (-not $NoRestore) {
    & $dotnet restore $testsPath --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) {
        throw "Restaurarea testelor a esuat cu codul $LASTEXITCODE."
    }

    & $dotnet restore $projectPath --runtime $Runtime --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) {
        throw "Restaurarea proiectului a esuat cu codul $LASTEXITCODE."
    }
}

& $dotnet build $testsPath --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Compilarea testelor a esuat cu codul $LASTEXITCODE."
}

& $dotnet run --project $testsPath --configuration Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Testele au esuat cu codul $LASTEXITCODE."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

& $dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $OutputDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    throw "Publicarea a esuat cu codul $LASTEXITCODE."
}

$hashes = Get-ChildItem -LiteralPath $OutputDirectory -File |
    Get-FileHash -Algorithm SHA256 |
    Select-Object Hash, @{Name = "File"; Expression = { Split-Path -Leaf $_.Path } }

$hashPath = Join-Path $OutputDirectory "SHA256SUMS.txt"
$hashes | ForEach-Object { "$($_.Hash)  $($_.File)" } |
    Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Host "Pachet creat in: $OutputDirectory"
Write-Host "Hash-uri: $hashPath"
