<#
.SYNOPSIS
Starts Groovefin and checks that it finishes starting: the window is shown and the app logs its
"Start-up took ..." line. Then it goes through the first run with UI Automation against a small fake
Jellyfin server: it adds the server, signs in, shows the home page, opens the folders and settings pages
and closes the window. Fails if the app exits too early, does not finish starting in time, cannot create
the equalizer effect, logs an unhandled exception, or does not exit cleanly after the window is closed.

.DESCRIPTION
On failure it prints the start-up error file, the end of the app's log and the latest error events
from the Windows Application log. Either way it closes the app again. The runner must be a clean
profile: the server page only shows on the first run.

The fake server answers the server information and sign-in requests, and returns three albums for
every item query, so the home page shows its lists.
#>
param(
    [Parameter(Mandatory)]
    [string] $Path,

    [int] $Seconds = 30,

    [int] $Port = 8096,

    # Where screenshots of checked pages go (uploaded by CI); none when empty.
    [string] $ScreenshotDir = ''
)

$ErrorActionPreference = 'Stop'

$data = Join-Path $env:LOCALAPPDATA 'Groovefin'
$logs = Join-Path $data 'Logs'
$started = Get-Date

function Get-AppLog {
    Get-ChildItem $logs -Filter *.log -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
}

function Write-Diagnostics {
    Write-Output '--- Start-up error file (with every thrown exception) ---'
    Get-Content (Join-Path $data 'startup-error.log') -Tail 400 -ErrorAction SilentlyContinue
    Write-Output '--- App log ---'
    if ($log = Get-AppLog) { Get-Content $log.FullName -Tail 100 }
    Write-Output '--- Fake server requests ---'
    $server | Receive-Job -ErrorAction SilentlyContinue
    Write-Output '--- Application event log (errors) ---'
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 2; StartTime = $started } -ErrorAction SilentlyContinue |
        Select-Object -First 5 |
        Format-List TimeCreated, ProviderName, Message
}

# The finally block at the end closes the app and the fake server.
function Stop-Test([string] $title, [string] $message) {
    Write-Output "::error title=$title::$message"
    Write-Diagnostics
    exit 1
}

# The fake Jellyfin server. It answers until the listener is stopped and writes one line per request.
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
$server = Start-ThreadJob -ArgumentList $listener -ScriptBlock {
    param($listener)
    $serverId = '0123456789abcdef0123456789abcdef'
    $albums = 1..3 | ForEach-Object {
        @{ Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa$_"; Name = "Album $_"; Type = 'MusicAlbum'; AlbumArtist = 'Artist'; ImageTags = @{} }
    }
    while ($listener.IsListening) {
        try { $context = $listener.GetContext() } catch { break }
        $request = $context.Request
        $path = $request.Url.AbsolutePath.ToLowerInvariant()
        $status = 200
        $body = switch -Regex ($path) {
            '^/system/info/public$' { @{ Id = $serverId; ServerName = 'Smoke test'; Version = '10.11.0'; ProductName = 'Jellyfin Server'; StartupWizardCompleted = $true } }
            '^/users/authenticatebyname$' { @{ User = @{ Id = 'fedcba9876543210fedcba9876543210'; Name = 'smoke'; ServerId = $serverId }; AccessToken = 'smoke-test-token'; ServerId = $serverId } }
            '^/quickconnect/enabled$' { $false }
            '^/userviews$' { @{ Items = @(@{ Id = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'; Name = 'Music'; Type = 'CollectionFolder'; CollectionType = 'music' }); TotalRecordCount = 1; StartIndex = 0 } }
            '/images/' { $status = 404; $null }
            '^/items$' { @{ Items = $albums; TotalRecordCount = $albums.Count; StartIndex = 0 } }
            default { @{ Items = @(); TotalRecordCount = 0; StartIndex = 0 } }
        }
        "$($request.HttpMethod) $($request.Url.PathAndQuery) -> $status"
        $context.Response.StatusCode = $status
        if ($null -ne $body) {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes((ConvertTo-Json $body -Depth 5 -Compress))
            $context.Response.ContentType = 'application/json'
            $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        $context.Response.Close()
    }
}

try {
    # The start-up error file then lists every exception thrown, also the ones XAML wraps in its own.
    $env:GROOVEFIN_TRACE_EXCEPTIONS = '1'
    $process = Start-Process -FilePath $Path -PassThru
    # Keeps the process handle open, so ExitCode is still known after the process has exited.
    $null = $process.Handle
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
        Stop-Test 'Start-up crash' "Groovefin exited with code $($process.ExitCode) while starting."
    }

    if (-not $ready) {
        Stop-Test 'Start-up incomplete' "Groovefin is running but did not finish starting within $Seconds seconds."
    }

    Write-Output "Groovefin started: $($ready.Line)"

    # The equalizer's audio effect: a native DLL that both manifests register (#65). The playback engine asks
    # Windows whether it can create it and logs the answer when it is created.
    $until = (Get-Date).AddSeconds(10)
    do {
        $equalizer = Select-String -Path $log.FullName -Pattern 'Equalizer effect is' -SimpleMatch | Select-Object -Last 1
        if (-not $equalizer) { Start-Sleep -Milliseconds 500 }
    } while (-not $equalizer -and (Get-Date) -lt $until)
    if (-not $equalizer -or $equalizer.Line -notmatch 'Equalizer effect is available') {
        Stop-Test 'Equalizer' "Windows could not create the equalizer effect: $($equalizer.Line)"
    }
    Write-Output 'The equalizer effect is registered.'

    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $automation = [System.Windows.Automation.AutomationElement]
    $scope = [System.Windows.Automation.TreeScope]
    $window = $automation::RootElement.FindFirst($scope::Children,
        [System.Windows.Automation.PropertyCondition]::new($automation::ProcessIdProperty, $process.Id))

    # Waits for an element of the window (pages appear after navigation) and returns it.
    function Find-Element([System.Windows.Automation.Condition] $condition, [string] $description) {
        $until = (Get-Date).AddSeconds(10)
        do {
            if ($element = $window.FindFirst($scope::Descendants, $condition)) { return $element }
            Start-Sleep -Milliseconds 500
        } while ((Get-Date) -lt $until -and -not $process.HasExited)
        Stop-Test 'Smoke test' "The $description was not found."
    }

    function Find-ById([string] $id) {
        Find-Element ([System.Windows.Automation.PropertyCondition]::new($automation::AutomationIdProperty, $id)) "element '$id'"
    }

    # By name and control type: the sign-in page's heading and button are both called "Sign in".
    function Find-Button([string] $name) {
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, $name),
            [System.Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
        Find-Element $condition "button '$name'"
    }

    function Test-Running([string] $step) {
        $unhandled = Select-String -Path (Get-AppLog).FullName -Pattern 'Unhandled' -SimpleMatch
        if ($process.HasExited -or $unhandled) {
            $state = if ($process.HasExited) { "exited with code $($process.ExitCode)" } else { 'logged an unhandled exception' }
            Stop-Test 'Smoke test' "Groovefin $state $step."
        }
    }

    $invoke = [System.Windows.Automation.InvokePattern]::Pattern

    # Saves the window as it is on screen to $ScreenshotDir\<name>.png, for a look at the layout.
    function Save-Screenshot([string] $name) {
        if (-not $ScreenshotDir) { return }
        try {
            Add-Type -AssemblyName System.Drawing
            $null = New-Item -ItemType Directory -Force -Path $ScreenshotDir
            $r = $window.Current.BoundingRectangle
            $bitmap = [System.Drawing.Bitmap]::new([int] $r.Width, [int] $r.Height)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $graphics.CopyFromScreen([int] $r.X, [int] $r.Y, 0, 0, $bitmap.Size)
            $bitmap.Save((Join-Path $ScreenshotDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
            $graphics.Dispose()
            $bitmap.Dispose()
            Write-Output "Saved screenshot $name.png"
        }
        catch {
            Write-Output "No screenshot ${name}: $($_.Exception.Message)"
        }
    }

    # Sets the text of a text box. A page that is still being shown may not offer the value pattern yet;
    # if it never does, the text is typed into the focused box.
    function Set-Text([System.Windows.Automation.AutomationElement] $element, [string] $text) {
        $pattern = $null
        $until = (Get-Date).AddSeconds(10)
        while (-not $element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $pattern) -and (Get-Date) -lt $until) {
            Start-Sleep -Milliseconds 500
        }
        if ($pattern) {
            $pattern.SetValue($text)
        }
        else {
            Add-Type -AssemblyName System.Windows.Forms
            $element.SetFocus()
            [System.Windows.Forms.SendKeys]::SendWait($text)
        }
    }

    # First run: add the server, which opens the sign-in page.
    Set-Text (Find-ById 'AddressBox') "http://127.0.0.1:$Port"
    (Find-Button 'Add server').GetCurrentPattern($invoke).Invoke()
    $userName = Find-ById 'UserNameBox'
    Test-Running 'while adding a server'
    Write-Output 'Added the fake server.'

    # The fake server accepts any user; the password may be empty.
    Set-Text $userName 'smoke'
    (Find-Button 'Sign in').GetCurrentPattern($invoke).Invoke()
    $null = Find-Button 'Refresh'
    Start-Sleep -Seconds 5
    Test-Running 'on the home page after signing in'
    Write-Output 'Signed in; the home page shows its lists.'

    # Folders: its breadcrumb once crashed the app (#144).
    $folders = Find-Element ([System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Folders'),
            [System.Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) "navigation item 'Folders'"
    $selection = $null
    if ($folders.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $selection)) { $selection.Select() }
    else { $folders.GetCurrentPattern($invoke).Invoke() }
    $null = Find-Button 'Up one folder'
    Start-Sleep -Seconds 2
    Test-Running 'on the folders page'
    Write-Output 'The folders page opened.'

    # Settings, with the equalizer's sliders.
    $settings = Find-Element ([System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Settings'),
            [System.Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) "navigation item 'Settings'"
    if ($settings.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $selection)) { $selection.Select() }
    else { $settings.GetCurrentPattern($invoke).Invoke() }
    $equalizerCard = Find-Element ([System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Equalizer')) "equalizer settings"
    Start-Sleep -Seconds 2
    Test-Running 'on the settings page'
    Write-Output 'The settings page opened with the equalizer.'

    # The band sliders: expanded and scrolled into view, each must have a visible size (#165).
    # The header text is named "Equalizer" too: expand the element that can be expanded.
    $pattern = $null
    $expanded = $false
    $named = $window.FindAll($scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Equalizer'))
    foreach ($element in @($equalizerCard) + @($named)) {
        Write-Output "Element 'Equalizer': $($element.Current.ControlType.ProgrammaticName), class $($element.Current.ClassName)"
        if (-not $expanded -and $element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $pattern)) {
            if ($element.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $scroll)) { $scroll.ScrollIntoView() }
            $pattern.Expand()
            $expanded = $true
        }
    }
    Write-Output "Expanded the equalizer: $expanded"
    Start-Sleep -Seconds 2
    $sliders = @($window.FindAll($scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Slider)))
    $band = $sliders | Where-Object { $_.Current.Name -eq '1 kHz' } | Select-Object -First 1
    $scroll = $null
    if ($band -and $band.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $scroll)) { $scroll.ScrollIntoView(); Start-Sleep -Seconds 1 }
    Save-Screenshot 'settings-equalizer'
    foreach ($slider in $sliders) {
        $r = $slider.Current.BoundingRectangle
        Write-Output ("Slider '{0}': {1:0}x{2:0} at {3:0},{4:0}, offscreen {5}" -f $slider.Current.Name, $r.Width, $r.Height, $r.X, $r.Y, $slider.Current.IsOffscreen)
    }
    if (-not $band) {
        Stop-Test 'Smoke test' "The equalizer slider '1 kHz' was not found ($($sliders.Count) sliders on the page)."
    }
    $bandRect = $band.Current.BoundingRectangle
    if ($band.Current.IsOffscreen -or $bandRect.Width -lt 16 -or $bandRect.Height -lt 100) {
        Stop-Test 'Smoke test' "The equalizer slider '1 kHz' has no visible size: $($bandRect.Width)x$($bandRect.Height)."
    }
    Test-Running 'with the equalizer expanded'
    Write-Output 'The equalizer sliders are visible.'

    # Close the window like a user: the app must shut down without errors and exit with code 0 (#145).
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    if (-not $process.WaitForExit(20000)) {
        Stop-Test 'Smoke test' 'Groovefin was still running 20 seconds after its window was closed.'
    }
    $unhandled = Select-String -Path (Get-AppLog).FullName -Pattern 'Unhandled' -SimpleMatch
    if ($unhandled -or $process.ExitCode -ne 0) {
        Stop-Test 'Smoke test' "Groovefin exited with code $($process.ExitCode) or logged an unhandled exception while closing."
    }
    Write-Output 'Closing the window shut Groovefin down cleanly.'
}
catch {
    Stop-Test 'Smoke test' "The test failed: $($_.Exception.Message)"
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }

    # Stopping the listener ends the server job's wait for the next request.
    $listener.Stop()
    $server | Wait-Job -Timeout 10 | Out-Null
    $server | Remove-Job -Force
}
