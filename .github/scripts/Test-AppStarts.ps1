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
    $artist = @{ Id = 'dddddddddddddddddddddddddddddddd'; Name = 'Artist'; Type = 'MusicArtist'; ImageTags = @{} }
    $albums = 1..3 | ForEach-Object {
        @{ Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa$_"; Name = "Album $_"; Type = 'MusicAlbum'; AlbumArtist = 'Artist'
           AlbumArtists = @(@{ Id = $artist.Id; Name = $artist.Name }); ImageTags = @{} }
    }
    $tracks = 1..2 | ForEach-Object {
        @{ Id = "ccccccccccccccccccccccccccccccc$_"; Name = "Track $_"; Type = 'Audio'; MediaType = 'Audio'; IndexNumber = $_
           AlbumId = $albums[0].Id; Album = 'Album 1'; AlbumArtist = 'Artist'; RunTimeTicks = 300000000; ImageTags = @{} }
    }

    # Thirty seconds of a quiet 440 Hz tone: 16-bit mono PCM at 22.05 kHz in a WAV file, for every stream.
    $rate = 22050
    $samples = $rate * 30
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([int] (36 + $samples * 2))
    $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $writer.Write([int] 16); $writer.Write([int16] 1); $writer.Write([int16] 1)
    $writer.Write([int] $rate); $writer.Write([int] ($rate * 2)); $writer.Write([int16] 2); $writer.Write([int16] 16)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([int] ($samples * 2))
    for ($i = 0; $i -lt $samples; $i++) { $writer.Write([int16] (3000 * [Math]::Sin(2 * [Math]::PI * 440 * $i / $rate))) }
    $writer.Flush()
    $wav = $stream.ToArray()
    while ($listener.IsListening) {
        try { $context = $listener.GetContext() } catch { break }
        $request = $context.Request
        $path = $request.Url.AbsolutePath.ToLowerInvariant()
        if ($path -eq '/items' -and $request.Url.Query -match 'includeItemTypes=Audio') { $path = '/items/tracks' }
        $status = 200
        $raw = $null
        $body = switch -Regex ($path) {
            '^/system/info/public$' { @{ Id = $serverId; ServerName = 'Smoke test'; Version = '10.11.0'; ProductName = 'Jellyfin Server'; StartupWizardCompleted = $true } }
            '^/users/authenticatebyname$' { @{ User = @{ Id = 'fedcba9876543210fedcba9876543210'; Name = 'smoke'; ServerId = $serverId }; AccessToken = 'smoke-test-token'; ServerId = $serverId } }
            '^/quickconnect/enabled$' { $false }
            '^/userviews$' { @{ Items = @(@{ Id = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'; Name = 'Music'; Type = 'CollectionFolder'; CollectionType = 'music' }); TotalRecordCount = 1; StartIndex = 0 } }
            '/images/' { $status = 404; $null }
            '^/items/tracks$' { @{ Items = $tracks; TotalRecordCount = $tracks.Count; StartIndex = 0 } }
            '^/items/[0-9a-f-]{32,36}$' { if ($path -eq "/items/$($artist.Id)") { $artist } else { $albums[0] } }
            '^/audio/[0-9a-f-]{32,36}/universal$' { $raw = $wav; $null }
            '^/items$' { @{ Items = $albums; TotalRecordCount = $albums.Count; StartIndex = 0 } }
            default { @{ Items = @(); TotalRecordCount = 0; StartIndex = 0 } }
        }
        "$($request.HttpMethod) $($request.Url.PathAndQuery) -> $status"
        $context.Response.StatusCode = $status
        if ($null -ne $raw) {
            $context.Response.ContentType = 'audio/wav'
            $context.Response.ContentLength64 = $raw.Length
            try { $context.Response.OutputStream.Write($raw, 0, $raw.Length) } catch { }
        }
        elseif ($null -ne $body) {
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

    # Selects a page in the navigation pane by its name.
    function Select-Navigation([string] $name) {
        $item = Find-Element ([System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, $name),
                [System.Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) "navigation item '$name'"
        $selection = $null
        if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $selection)) { $selection.Select() }
        else { $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    }

    # GitHub's Windows runners have no sound card: Media Foundation then fails at the audio output
    # (0xC00D36FA, "failed to create mediasink"), after the equalizer effect has been negotiated.
    $hasAudio = [bool] (Get-CimInstance Win32_SoundDevice -ErrorAction SilentlyContinue)

    # With an audio device, the position slider must move by at least two seconds within 15 seconds and no
    # track may fail. Without one, the only failure allowed is the missing audio output: the stream got past
    # the equalizer effect. Either way the log must not show an exception from .NET code inside Media
    # Foundation (0x8013xxxx, the effect) or the engine taking the effect out (#167).
    function Test-Playing([string] $step) {
        $range = $position.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
        $start = $range.Current.Value
        $until = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 500
            $text = Get-Content (Get-AppLog).FullName -Raw
            $done = $range.Current.Value -ge $start + 2 -or (-not $hasAudio -and $text -match '0xC00D36FA')
        } while (-not $done -and (Get-Date) -lt $until -and -not $process.HasExited)

        if ($text -match '\(0x8013[0-9A-F]{4}\)' -or $text -match 'Equalizer effect failed') {
            Stop-Test 'Playback' "The equalizer effect failed $step`: $(($text -split "`n" | Select-String '0x8013|Equalizer effect failed' | Select-Object -First 1).Line)"
        }
        if ($hasAudio) {
            $failed = Select-String -Path (Get-AppLog).FullName -Pattern 'Playback of' -SimpleMatch
            if ($failed) { Stop-Test 'Playback' "Playback failed $step`: $($failed[0].Line)" }
            if ($range.Current.Value -lt $start + 2) { Stop-Test 'Playback' "The position did not move $step (at $($range.Current.Value) s)." }
        }
        elseif ($text -notmatch '0xC00D36FA') {
            Stop-Test 'Playback' "No sound card here, so playback should fail at the audio output (0xC00D36FA) $step, but the log does not show it."
        }
        Test-Running $step
    }

    function Test-Running([string] $step) {
        $unhandled = Select-String -Path (Get-AppLog).FullName -Pattern 'Unhandled' -SimpleMatch
        if ($process.HasExited -or $unhandled) {
            $state = if ($process.HasExited) { "exited with code $($process.ExitCode)" } else { 'logged an unhandled exception' }
            Stop-Test 'Smoke test' "Groovefin $state $step."
        }
    }

    $invoke = [System.Windows.Automation.InvokePattern]::Pattern

    # Counts the colours on screen within an element: one flat colour means it draws nothing.
    function Get-ColourCount([System.Windows.Automation.AutomationElement] $element) {
        Add-Type -AssemblyName System.Drawing
        $r = $element.Current.BoundingRectangle
        $bitmap = [System.Drawing.Bitmap]::new([int] $r.Width, [int] $r.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen([int] $r.X, [int] $r.Y, 0, 0, $bitmap.Size)
        $colours = [System.Collections.Generic.HashSet[int]]::new()
        for ($x = 0; $x -lt $bitmap.Width; $x += 2) {
            for ($y = 0; $y -lt $bitmap.Height; $y += 2) { $null = $colours.Add($bitmap.GetPixel($x, $y).ToArgb()) }
        }
        $graphics.Dispose()
        $bitmap.Dispose()
        $colours.Count
    }

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
            $file = Join-Path $ScreenshotDir "$name.png"
            $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
            $graphics.Dispose()
            $bitmap.Dispose()
            Write-Output "Saved screenshot $name.png"
            if ($env:GROOVEFIN_SCREENSHOT_TO_LOG -eq '1') {
                # For a look without downloading the artifact.
                $text = [Convert]::ToBase64String([IO.File]::ReadAllBytes($file))
                for ($i = 0; $i -lt $text.Length; $i += 4000) {
                    Write-Output "SCREENSHOT $name $($text.Substring($i, [Math]::Min(4000, $text.Length - $i)))"
                }
            }
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

    # Songs: its header buttons once could not find their style and the page did not open (#171).
    Select-Navigation 'Songs'
    $null = Find-Button 'Columns'
    Start-Sleep -Seconds 2
    Test-Running 'on the songs page'
    Write-Output 'The songs page opened.'

    # Playback (#167): an album from Home, played through the equalizer effect. The position must move,
    # and no track may fail, nor may the engine have to take the effect out to play it.
    Select-Navigation 'Home'
    $until = (Get-Date).AddSeconds(10)
    do {
        $album = @($window.FindAll($scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) |
            Where-Object { $_.Current.Name -like 'Album 1*' } | Select-Object -First 1
        if (-not $album) { Start-Sleep -Milliseconds 500 }
    } while (-not $album -and (Get-Date) -lt $until)
    if (-not $album) { Stop-Test 'Smoke test' "The album 'Album 1' was not found on the home page." }
    $pattern = $null
    if ($album.TryGetCurrentPattern($invoke, [ref] $pattern)) { $pattern.Invoke() }
    else {
        Add-Type -AssemblyName System.Windows.Forms
        $album.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    }
    (Find-Button 'Play album').GetCurrentPattern($invoke).Invoke()
    $position = Find-Element ([System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Position'),
            [System.Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Slider))) "position slider"
    Test-Playing 'after pressing Play album'
    if ($hasAudio) { Write-Output 'Playback runs through the equalizer effect.' }
    else { Write-Output 'No sound card: the stream passed the equalizer effect and stopped only at the audio output.' }

    # The album's artist link opens the artist page (#172).
    $artistLink = Find-Element ([System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Artist'),
            [System.Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Hyperlink))) "artist link on the album page"
    $artistLink.GetCurrentPattern($invoke).Invoke()
    $null = Find-ById 'ArtistPlayButton'
    Test-Running 'on the artist page'
    Write-Output "The album's artist link opened the artist page."

    # Back and forward (#176): the mouse's back button over the artist page's Play button (a control that
    # handles presses itself), its forward button, then Alt+Left.
    Add-Type -Namespace Smoke -Name Mouse -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, System.UIntPtr extra);
'@
    function Send-MouseButton([System.Windows.Automation.AutomationElement] $over, [uint32] $button) {
        $r = $over.Current.BoundingRectangle
        $null = [Smoke.Mouse]::SetCursorPos([int] ($r.X + $r.Width / 2), [int] ($r.Y + $r.Height / 2))
        [Smoke.Mouse]::mouse_event(0x0080, 0, 0, $button, [UIntPtr]::Zero) # XDOWN
        [Smoke.Mouse]::mouse_event(0x0100, 0, 0, $button, [UIntPtr]::Zero) # XUP
    }
    Send-MouseButton (Find-ById 'ArtistPlayButton') 1
    $playAlbum = Find-Button 'Play album'
    Send-MouseButton $playAlbum 2
    $null = Find-ById 'ArtistPlayButton'
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait('%{LEFT}')
    $null = Find-Button 'Play album'
    Test-Running 'after going back and forward'
    Write-Output 'The mouse back and forward buttons and Alt+Left move through the pages.'

    # Settings, with the equalizer's sliders.
    Select-Navigation 'Settings'
    $equalizerCard = Find-Element ([System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Equalizer')) "equalizer settings"
    Start-Sleep -Seconds 2
    Test-Running 'on the settings page'
    Write-Output 'The settings page opened with the equalizer.'

    # The band sliders: expanded and scrolled into view, each must have a visible size (#165).
    # The header text is named "Equalizer" too: expand the element that can be expanded.
    $pattern = $null
    $scroll = $null
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
    $visual = $null
    if ($window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref] $visual)) { $visual.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized) }
    Start-Sleep -Seconds 2
    $sliders = @($window.FindAll($scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Slider)))
    $band = $sliders | Where-Object { $_.Current.Name -eq '1 kHz' } | Select-Object -First 1
    if ($band -and $band.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $scroll)) { $scroll.ScrollIntoView(); Start-Sleep -Seconds 1 }
    Save-Screenshot 'settings-equalizer'
    foreach ($slider in $sliders) {
        if ($slider.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $scroll)) { $scroll.ScrollIntoView(); Start-Sleep -Milliseconds 300 }
        $r = $slider.Current.BoundingRectangle
        $colours = if ($slider.Current.IsOffscreen) { 0 } else { Get-ColourCount $slider }
        Write-Output ("Slider '{0}': {1:0}x{2:0} at {3:0},{4:0}, offscreen {5}, {6} colours" -f $slider.Current.Name, $r.Width, $r.Height, $r.X, $r.Y, $slider.Current.IsOffscreen, $colours)
    }
    if (-not $band) {
        Stop-Test 'Smoke test' "The equalizer slider '1 kHz' was not found ($($sliders.Count) sliders on the page)."
    }
    if ($band.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $scroll)) { $scroll.ScrollIntoView(); Start-Sleep -Milliseconds 300 }
    $bandRect = $band.Current.BoundingRectangle
    if ($band.Current.IsOffscreen -or $bandRect.Width -lt 16 -or $bandRect.Height -lt 100) {
        Stop-Test 'Smoke test' "The equalizer slider '1 kHz' has no visible size: $($bandRect.Width)x$($bandRect.Height)."
    }
    if ((Get-ColourCount $band) -lt 3) {
        Stop-Test 'Smoke test' "The equalizer slider '1 kHz' draws nothing: its area is one flat colour."
    }
    Test-Running 'with the equalizer expanded'
    Write-Output 'The equalizer sliders are visible.'

    # The equalizer on while the track plays: the effect processes it now, and it must go on playing.
    $switch = $named | Where-Object { $_.Current.ClassName -eq 'ToggleSwitch' } | Select-Object -First 1
    $toggle = $null
    if ($switch -and $switch.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref] $toggle)) {
        $toggle.Toggle()
        Test-Playing 'with the equalizer on'
        Write-Output 'The equalizer is on without errors.'
    }
    else {
        Stop-Test 'Smoke test' 'The equalizer switch was not found.'
    }

    # Keyboard shortcuts (#68): the change dialog records Ctrl+Alt+N for "Next track", Enter saves it (the
    # dialog opens with focus on Cancel), and "Reset all" brings Ctrl+Right back. The rows are named
    # "<what it does>, <keys>".
    $expanded = $false
    $named = $window.FindAll($scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Keyboard shortcuts'))
    foreach ($element in @($named)) {
        if (-not $expanded -and $element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $pattern)) {
            if ($element.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $scroll)) { $scroll.ScrollIntoView() }
            $pattern.Expand()
            $expanded = $true
        }
    }
    if (-not $expanded) { Stop-Test 'Smoke test' 'The keyboard shortcuts could not be expanded.' }
    $change = Find-Button 'Change the keys for Next track'
    if ($change.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $scroll)) { $scroll.ScrollIntoView() }
    $null = Find-Element ([System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Next track, Ctrl+Right')) 'the shortcut row before the change'
    $change.GetCurrentPattern($invoke).Invoke()
    $null = Find-Element ([System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Change keys')) 'shortcut dialog'
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait('^%n')
    $null = Find-Element ([System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Ctrl+Alt+N')) 'the recorded keys in the dialog'
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $null = Find-Element ([System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Next track, Ctrl+Alt+N')) 'the changed shortcut'
    (Find-Button 'Reset all').GetCurrentPattern($invoke).Invoke()
    $null = Find-Element ([System.Windows.Automation.PropertyCondition]::new($automation::NameProperty, 'Next track, Ctrl+Right')) 'the default shortcut after Reset all'
    Test-Running 'after changing a shortcut'
    Write-Output 'A shortcut got new keys in the change dialog and Reset all restored it.'

    # Mini player (#181, #183): Ctrl+M makes the window a small one on top, 420x140 (cover left, text right).
    # It can be resized, but no larger than 720x200; Esc brings the full window back.
    [System.Windows.Forms.SendKeys]::SendWait('^m')
    $exitMini = Find-Button 'Back to the full window'
    Start-Sleep -Seconds 1
    $mini = $window.Current.BoundingRectangle
    Write-Output ("Mini player window: {0:0}x{1:0}" -f $mini.Width, $mini.Height)
    if ([Math]::Abs($mini.Width - 420) -gt 8 -or [Math]::Abs($mini.Height - 140) -gt 8) {
        Stop-Test 'Smoke test' ("The mini player is {0:0}x{1:0}; it should be 420x140." -f $mini.Width, $mini.Height)
    }
    # Resized like Windows does it (UI Automation's resize does nothing on a WinUI window): SetWindowPos goes
    # through the window's size limits, the same ones a drag on its edges meets.
    Add-Type -Namespace Smoke -Name Window -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
'@
    $hwnd = [IntPtr] $window.Current.NativeWindowHandle
    # SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE
    $null = [Smoke.Window]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 520, 170, 0x16)
    Start-Sleep -Milliseconds 500
    $resized = $window.Current.BoundingRectangle
    $null = [Smoke.Window]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 2000, 2000, 0x16)
    Start-Sleep -Milliseconds 500
    $largest = $window.Current.BoundingRectangle
    Write-Output ("Mini player resized: {0:0}x{1:0}, at most {2:0}x{3:0}" -f $resized.Width, $resized.Height, $largest.Width, $largest.Height)
    if ([Math]::Abs($resized.Width - 520) -gt 24 -or $largest.Width -gt 744 -or $largest.Height -gt 224) {
        Stop-Test 'Smoke test' ("The mini player should resize to 520x170 and stop at 720x200, not {0:0}x{1:0} and {2:0}x{3:0}." -f $resized.Width, $resized.Height, $largest.Width, $largest.Height)
    }
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    $null = Select-Navigation 'Settings'
    Test-Running 'after the mini player'
    Write-Output 'The mini player opened at 420x140, resized within its limits, and Esc left it.'

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
