[CmdletBinding()]
param(
    [string]$SourceDirectory = "",
    [string]$InstallDirectory = "C:\Program Files\PimaFilter\Monitor",
    [switch]$StartMonitor
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "app"
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Porniti PowerShell cu Run as administrator."
}

$source = [IO.Path]::GetFullPath($SourceDirectory)
$executable = Join-Path $source "PimaFilter.Monitor.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Nu gasesc PimaFilter.Monitor.exe in $source"
}

$installFull = [IO.Path]::GetFullPath($InstallDirectory)
if (-not $installFull.EndsWith("\PimaFilter\Monitor", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Director de instalare neasteptat: $installFull"
}

New-Item -ItemType Directory -Path $installFull -Force | Out-Null
$installedExe = Join-Path $installFull "PimaFilter.Monitor.exe"
$runningMonitors = @(
    Get-Process -Name "PimaFilter.Monitor" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $installedExe }
)
if ($runningMonitors.Count -gt 0) {
    $runningMonitors | Stop-Process -Force
    $runningMonitors | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

$copyError = $null
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        Copy-Item -Path (Join-Path $source "*") -Destination $installFull -Recurse -Force
        $copyError = $null
        break
    }
    catch [IO.IOException] {
        $copyError = $_
        if ($attempt -lt 5) {
            Start-Sleep -Milliseconds 600
        }
    }
}
if ($null -ne $copyError) {
    throw $copyError
}

$shortcutPath = "C:\Users\Public\Desktop\PIMA Signal Filter Monitor.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExe
$shortcut.IconLocation = "$installedExe,0"
$shortcut.WorkingDirectory = $installFull
$shortcut.Description = "Monitor si administrare blacklist pentru PIMA Signal Filter"
$shortcut.Save()
Remove-Item -LiteralPath "C:\Users\Public\Desktop\SurGard Signal Filter Monitor.lnk" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "C:\Users\Public\Desktop\PIMA Filter Monitor.lnk" -Force -ErrorAction SilentlyContinue

Write-Host "Monitor instalat: $installedExe"
Write-Host "Scurtatura: $shortcutPath"
Write-Host "Serviciul PimaFilter nu a fost oprit sau modificat."

if ($StartMonitor) {
    Start-Process -FilePath $installedExe
}
