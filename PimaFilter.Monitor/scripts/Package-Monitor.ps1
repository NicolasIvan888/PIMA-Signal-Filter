[CmdletBinding()]
param(
    [string]$Version = "0.5.0",
    [string]$Runtime = "win-x64",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$projectPath = Join-Path $projectRoot "PimaFilter.Monitor.csproj"
$testsPath = Join-Path $workspaceRoot "PimaFilter.Monitor.Tests\PimaFilter.Monitor.Tests.csproj"
$nugetConfig = Join-Path $workspaceRoot "NuGet.Config"
$dotnetLocal = Join-Path $workspaceRoot ".dotnet-sdk\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $dotnetLocal -PathType Leaf) { $dotnetLocal } else { "dotnet" }
$artifactRoot = Join-Path $workspaceRoot "artifacts\PimaFilter-Monitor-$Runtime-$Version"
$deploymentRoot = Join-Path $workspaceRoot "deployment\PimaFilter-Monitor-$Version"
$zipPath = Join-Path $workspaceRoot "deployment\PimaFilter-Monitor-$Version.zip"

$workspaceFull = [IO.Path]::GetFullPath($workspaceRoot).TrimEnd('\') + '\'
foreach ($target in @($artifactRoot, $deploymentRoot, $zipPath)) {
    $targetFull = [IO.Path]::GetFullPath($target)
    if (-not $targetFull.StartsWith($workspaceFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Tinta trebuie sa ramana in workspace: $targetFull"
    }
}

if (-not $NoRestore) {
    & $dotnet restore $testsPath --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) { throw "Restaurarea testelor a esuat." }
    & $dotnet restore $projectPath --runtime $Runtime --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) { throw "Restaurarea monitorului a esuat." }
}
& $dotnet build $testsPath --configuration Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { throw "Compilarea testelor a esuat." }
& $dotnet run --project $testsPath --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Testele monitorului au esuat." }

if (Test-Path -LiteralPath $artifactRoot -PathType Container) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
& $dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $artifactRoot `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Publicarea monitorului a esuat." }

if (Test-Path -LiteralPath $deploymentRoot -PathType Container) {
    Remove-Item -LiteralPath $deploymentRoot -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $deploymentRoot "app") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $deploymentRoot "scripts") -Force | Out-Null
Copy-Item -Path (Join-Path $artifactRoot "*") -Destination (Join-Path $deploymentRoot "app") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Install-Monitor.ps1") -Destination (Join-Path $deploymentRoot "scripts")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Uninstall-Monitor.ps1") -Destination (Join-Path $deploymentRoot "scripts")
Copy-Item -LiteralPath (Join-Path $projectRoot "INSTRUCTIUNE.txt") -Destination $deploymentRoot

if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -LiteralPath $deploymentRoot -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
"$($hash.Hash)  $(Split-Path -Leaf $zipPath)" |
    Set-Content -LiteralPath ($zipPath + ".sha256.txt") -Encoding ascii

Write-Host "Pachet monitor: $zipPath"
Write-Host "SHA256: $($hash.Hash)"
