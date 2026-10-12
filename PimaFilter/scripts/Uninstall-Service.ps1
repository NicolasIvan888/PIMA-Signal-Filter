[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory = "C:\Program Files\PimaFilter",
    [string]$ServiceName = "PimaFilter",
    [string]$DisplayName = "PIMA Signal Filter"
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Porniti PowerShell cu Run as administrator."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service -and $PSCmdlet.ShouldProcess($ServiceName, "Dezinstalare serviciu")) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(20))
    }
    & sc.exe delete $ServiceName | Out-Null
}

Get-NetFirewallRule -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -like "$DisplayName TCP *" -or $_.DisplayName -like "PIMA Guard Filter TCP *" } |
    Remove-NetFirewallRule

if (Test-Path -LiteralPath $InstallDirectory -PathType Container) {
    Write-Host "Fisierele instalate raman in $InstallDirectory pentru recuperare manuala."
}
Write-Host "Datele si blacklist-ul din C:\ProgramData\PimaFilter nu au fost sterse."
