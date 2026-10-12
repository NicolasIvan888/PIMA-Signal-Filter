[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory = "C:\Program Files\PimaFilter\Monitor"
)

$ErrorActionPreference = "Stop"
$installFull = [IO.Path]::GetFullPath($InstallDirectory)
if (-not $installFull.EndsWith("\PimaFilter\Monitor", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Director de dezinstalare neasteptat: $installFull"
}

$shortcutPath = "C:\Users\Public\Desktop\PIMA Signal Filter Monitor.lnk"
if ($PSCmdlet.ShouldProcess($shortcutPath, "Sterge scurtatura")) {
    Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "C:\Users\Public\Desktop\PIMA Filter Monitor.lnk" -Force -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $installFull -PathType Container) {
    if ($PSCmdlet.ShouldProcess($installFull, "Sterge monitorul")) {
        Remove-Item -LiteralPath $installFull -Recurse -Force
    }
}

Write-Host "Serviciul PimaFilter si datele din ProgramData nu au fost modificate."
