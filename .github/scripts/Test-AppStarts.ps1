<#
.SYNOPSIS
Starts Groovefin and checks that it finishes starting: the window is shown and the app logs its
"Start-up took ..." line. Then it uses the first-run server page through UI Automation: it adds an
unreachable server, so the page shows its progress ring while the connection is retried. Fails if the
app exits, does not finish starting in time, or logs an unhandled XAML exception.

.DESCRIPTION
On failure it prints the start-up error file, the end of the app's log and the latest error events
from the Windows Application log. Either way it closes the app again. The runner must be a clean
profile: the server page only shows on the first run.
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

# Add an unreachable server (nothing listens on the discard port): the progress ring spins while the
# connection is retried, then the page shows its inline error.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$automation = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$window = $automation::RootElement.FindFirst($scope::Children,
    [System.Windows.Automation.PropertyCondition]::new($automation::ProcessIdProperty, $process.Id))
$box = $window.FindFirst($scope::Descendants,
    [System.Windows.Automation.PropertyCondition]::new($automation::AutomationIdProperty, 'AddressBox'))
$add = $window.FindFirst($scope::Descendants,
    [System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Add server'))
if (-not $box -or -not $add) {
    Write-Output '::error title=Smoke test::The server page (address box and Add server button) was not found.'
    Write-Diagnostics
    Stop-Process -Id $process.Id -Force
    exit 1
}

$box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('http://127.0.0.1:9')
$add.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 10

$log = Get-AppLog
$unhandled = Select-String -Path $log.FullName -Pattern 'Unhandled' -SimpleMatch
if ($process.HasExited -or $unhandled) {
    $state = if ($process.HasExited) { "exited with code $($process.ExitCode)" } else { 'logged an unhandled exception' }
    Write-Output "::error title=Server page::Groovefin $state while adding a server."
    Write-Diagnostics
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    exit 1
}

Write-Output 'Adding an unreachable server showed the progress ring and its error without problems.'
Stop-Process -Id $process.Id -Force
