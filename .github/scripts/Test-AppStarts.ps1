<#
.SYNOPSIS
Starts Groovefin and checks that it finishes starting: the window is shown and the app logs its
"Start-up took ..." line. Fails if the app exits (a crash) or does not get there in time.

.DESCRIPTION
On failure it prints the start-up error file, the end of the app's log and the latest error events
from the Windows Application log. Either way it closes the app again.
#>
param(
    [Parameter(Mandatory)]
    [string] $Path,

    [int] $Seconds = 30
)

$ErrorActionPreference = 'Stop'

$data = Join-Path $env:LOCALAPPDATA 'Groovefin'
$logs = Join-Path $data 'Logs'
$started = Get-Date

function Get-AppLog {
    Get-ChildItem $logs -Filter *.log -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
}

function Write-Diagnostics {
    Write-Output '--- Start-up error ---'
    Get-Content (Join-Path $data 'startup-error.log') -Tail 200 -ErrorAction SilentlyContinue
    Write-Output '--- App log ---'
    if ($log = Get-AppLog) { Get-Content $log.FullName -Tail 100 }
    Write-Output '--- Application event log (errors) ---'
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 2; StartTime = $started } -ErrorAction SilentlyContinue |
        Select-Object -First 5 |
        Format-List TimeCreated, ProviderName, Message
}

$process = Start-Process -FilePath $Path -PassThru
$deadline = $started.AddSeconds($Seconds)
$ready = $null

while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
    Start-Sleep -Seconds 1
    if ($log = Get-AppLog) {
        $ready = Select-String -Path $log.FullName -Pattern 'Start-up took' -SimpleMatch | Select-Object -Last 1
        if ($ready) { break }
    }
}

if ($process.HasExited) {
    Write-Output "::error title=Start-up crash::Groovefin exited with code $($process.ExitCode) while starting."
    Write-Diagnostics
    exit 1
}

if (-not $ready) {
    Write-Output "::error title=Start-up incomplete::Groovefin is running but did not finish starting within $Seconds seconds."
    Write-Diagnostics
    Stop-Process -Id $process.Id -Force
    exit 1
}

Write-Output "Groovefin started: $($ready.Line)"
Stop-Process -Id $process.Id -Force
