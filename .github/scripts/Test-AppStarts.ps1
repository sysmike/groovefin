<#
.SYNOPSIS
Starts Groovefin and fails if it exits within the first seconds, which means it crashed on start.

.DESCRIPTION
On a crash it prints the start-up error file, the end of the app's log and the latest error events
from the Windows Application log. Otherwise it closes the app again.
#>
param(
    [Parameter(Mandatory)]
    [string] $Path,

    [int] $Seconds = 20
)

$ErrorActionPreference = 'Stop'

$logs = Join-Path $env:LOCALAPPDATA 'Groovefin\Logs'
$started = Get-Date
$process = Start-Process -FilePath $Path -PassThru
Start-Sleep -Seconds $Seconds

if ($process.HasExited) {
    Write-Output "::error title=Start-up crash::Groovefin exited with code $($process.ExitCode) within $Seconds seconds."
    Write-Output '--- Start-up error ---'
    Get-Content (Join-Path $env:LOCALAPPDATA 'Groovefin\startup-error.log') -Tail 200 -ErrorAction SilentlyContinue
    Write-Output '--- App log ---'
    Get-ChildItem $logs -Filter *.log -ErrorAction SilentlyContinue | Get-Content -Tail 100
    Write-Output '--- Application event log (errors) ---'
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 2; StartTime = $started } -ErrorAction SilentlyContinue |
        Select-Object -First 5 |
        Format-List TimeCreated, ProviderName, Message
    exit 1
}

Write-Output "Groovefin is still running after $Seconds seconds."
Stop-Process -Id $process.Id -Force
Get-ChildItem $logs -Filter *.log -ErrorAction SilentlyContinue | Get-Content -Tail 20
