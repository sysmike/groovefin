# Architecture

Groovefin is a native Jellyfin music player for Windows 11. It is written in C# on .NET 10 with
WinUI 3 (Windows App SDK). This document describes how the solution is split, which project may
depend on which, and the conventions every feature follows.

## Goals

- **Native first.** Fluent design and stock WinUI 3 controls. No WebView2 for UI.
- **Testable.** All logic that does not need WinUI (domain, ViewModels, queue, API mapping) runs and
  is unit-tested on any OS.
- **Replaceable edges.** The Jellyfin SDK and the Windows media stack sit behind interfaces. This
  makes a libmpv engine or a second server backend possible later.

## Projects

| Project | Target | Responsibility |
|---|---|---|
| `Groovefin.App` | `net10.0-windows10.0.22000.0`, WinUI 3, single-project MSIX | Composition root (generic host + DI), XAML views, styles, `.resw` resources, platform services (navigation, dialogs, theme, dispatcher, credentials, settings storage location). |
| `Groovefin.Core` | `net10.0` | Domain models, service interfaces, ViewModels, `QueueManager`, settings model, messages. **No WinUI or Windows references.** |
| `Groovefin.Audio` | `net10.0` | The equalizer's signal processing (peaking filters, parameters), without dependencies. Used by Core and by the audio effect. |
| `Groovefin.Audio.Effects` | `net10.0-windows10.0.26100.0` (minimum 10.0.22000), C#/WinRT component, NativeAOT | The equalizer as a Media Foundation audio effect (`IBasicAudioEffect`). Published as a native DLL next to `Groovefin.exe`; see [Audio effects](#audio-effects). |
| `Groovefin.Jellyfin` | `net10.0` | Implements the Core API interfaces with `Jellyfin.Sdk`: authentication, DTO→domain mapping, stream URLs, image URLs, playback reporting, resilient HTTP pipeline. |
| `Groovefin.Playback.Windows` | `net10.0-windows10.0.22000.0` | `IPlaybackEngine` implementation using `Windows.Media.Playback.MediaPlayer` + `MediaPlaybackList`. System Media Transport Controls. |
| `Groovefin.Core.Tests` | `net10.0` | Unit tests for Core (ViewModels, queue, parsers). |
| `Groovefin.Jellyfin.Tests` | `net10.0` | Unit tests for the Jellyfin layer against WireMock.Net fakes. |
| `Groovefin.Audio.Effects.Tests` | `net10.0-windows10.0.26100.0` | Windows only: sends a tone through the published NativeAOT effect with Media Foundation's transcoder and measures the level. |

## Component diagram

```mermaid
flowchart LR
    subgraph App["Groovefin.App (WinUI 3)"]
        Views["Views / XAML pages"]
        Shell["Shell window<br/>Mica · TitleBar · NavigationView"]
        PlatformSvc["Platform services<br/>Navigation · Dialog · Theme · Dispatcher<br/>Credential store · Settings storage"]
        Host["Generic host / DI<br/>Serilog"]
    end

    subgraph Core["Groovefin.Core (net10.0)"]
        VMs["ViewModels<br/>(CommunityToolkit.Mvvm)"]
        Queue["QueueManager"]
        Abstractions["Interfaces<br/>IJellyfinClient · ILibraryService · IPlaylistService<br/>ISearchService · ILyricsService · IPlaybackReporter<br/>IPlaybackEngine · INavigationService · IDialogService<br/>IThemeService · ISettingsService · IDispatcher"]
        Models["Domain models"]
    end

    subgraph Jellyfin["Groovefin.Jellyfin (net10.0)"]
        Sdk["Jellyfin.Sdk wrapper"]
        Mapping["DTO → domain mapping"]
        Http["HttpClient + resilience pipeline"]
    end

    subgraph Playback["Groovefin.Playback.Windows"]
        Engine["MediaPlayerEngine<br/>MediaPlaybackList (gapless)"]
        Smtc["SMTC integration"]
    end

    Server[("Jellyfin server")]
    Windows[("Windows media stack")]

    Views --> VMs
    Shell --> VMs
    Host --> PlatformSvc
    PlatformSvc -. implements .-> Abstractions
    VMs --> Abstractions
    VMs --> Queue
    VMs --> Models
    Sdk -. implements .-> Abstractions
    Sdk --> Mapping --> Models
    Sdk --> Http --> Server
    Engine -. implements .-> Abstractions
    Engine --> Windows
    Smtc --> Windows
```

## Dependency rules

```mermaid
flowchart TD
    App[Groovefin.App] --> Core[Groovefin.Core]
    App --> Jellyfin[Groovefin.Jellyfin]
    App --> Playback[Groovefin.Playback.Windows]
    App -. native build .-> Effects[Groovefin.Audio.Effects]
    Jellyfin --> Core
    Playback --> Core
    Core --> Audio[Groovefin.Audio]
    Effects --> Audio
    CoreTests[Groovefin.Core.Tests] --> Core
    JellyfinTests[Groovefin.Jellyfin.Tests] --> Jellyfin
    EffectsTests[Groovefin.Audio.Effects.Tests] --> Playback
    EffectsTests -. native build .-> Effects
```

1. `Groovefin.Core` references no other Groovefin project but `Groovefin.Audio` (plain signal processing,
   no dependencies), and no WinUI / WinRT API.
2. `Groovefin.Jellyfin` and `Groovefin.Playback.Windows` reference only `Groovefin.Core`. They never
   reference each other.
3. Only `Groovefin.App` references everything. It is the only place that registers implementations
   in DI.
4. ViewModels never use `Jellyfin.Sdk` types. The Jellyfin layer maps DTOs to Core domain models
   (`Album`, `Artist`, `Track`, `Playlist`, ...) before they leave the project.
5. Views bind to ViewModels with `x:Bind`. Views contain no business logic. Code-behind is limited to
   pure view concerns (animations, focus, drag visuals).
6. Every NuGet package the app ships has a license notice in `ThirdPartyNotices` (Core), which the
   About section of Settings lists. `ThirdPartyNoticesTests` checks the list against
   `src/Groovefin.App/packages.lock.json`, so adding a dependency without its notice fails CI.

## Key abstractions

| Interface | Lives in | Implemented in | Purpose |
|---|---|---|---|
| `IJellyfinClient` | Core | Jellyfin | Connection, authentication, server info. Entry point to the focused services below. |
| `ILibraryService` | Core | Jellyfin | Albums, artists, tracks, genres, folders, favourites, instant mix. |
| `IPlaylistService` | Core | Jellyfin | Playlist CRUD and track editing. |
| `ISearchService` | Core | Jellyfin | Grouped search and suggestions. |
| `ILyricsService` | Core | Jellyfin | Lyrics (synced LRC and plain text). |
| `IPlaybackReporter` | Core | Jellyfin | `/Sessions/Playing`, `/Progress`, `/Stopped`. |
| `IPlaybackEngine` | Core | Playback.Windows | Load, play, pause, seek, volume. Queues the next source (`SetNext`) and moves on to it without a gap (`MediaPlaybackList` with the current and the next item). Optional crossfade (`Crossfade`): `PlaybackEngineBase` starts the next source on a second player near the end and fades the two (equal power); tracks of the same album stay gapless. Events for position, state and track changes. |
| `QueueManager` | Core | — (concrete, pure) | Queue order, shuffle with stable original order, repeat modes, history. |
| `IPlayer` | Core | Core (`Player`) | Queue and engine together: play/shuffle/queue tracks, transport, auto-advance, skipping failed tracks, keeping the engine's next source on the queue's next entry (gapless). Stream URLs come from `IStreamUrlProvider` with the streaming settings for the active server. |
| `ISystemMediaControls` | Core | Playback.Windows | Media keys and the Windows media flyout (SMTC of the engine's `MediaPlayer`, its command manager off). `SystemMediaControlsSync` (Core) shows the playing track, status and timeline and sends the buttons to `IPlayer`. |
| `IAudioEqualizer` | Core | Playback.Windows | The equalizer in the audio path: whether the effect could be added, and its parameters. `EqualizerSync` (Core) passes the settings on. |
| `IStreamUrlProvider` | Core | Jellyfin | `/Audio/{id}/universal` URLs (direct play or transcode, bitrate limit). |
| `INavigationService` | Core | App | Navigate by ViewModel type + parameter, back stack. |
| `IDialogService` | Core | App | Content dialogs, confirmations, pickers. |
| `IThemeService` | Core | App | Light / dark / system theme, density. |
| `ISettingsService` | Core | Core + App storage location | Strongly typed settings persisted as JSON. |
| `IDispatcher` | Core | App | Marshal work to the UI thread. The only way code outside views touches the UI thread. |
| `ICredentialStore` | Core | App | Access tokens in `PasswordVault`. |

## Audio effects

`MediaPlayer` takes audio effects only as registered WinRT classes (`AddAudioEffect(classId, …)`):
Media Foundation creates them with `RoActivateInstance`. So the equalizer runs as follows:

- `Groovefin.Audio.Effects` implements `IBasicAudioEffect` with C#/WinRT authoring. `Groovefin.App`
  publishes it with NativeAOT for its runtime identifier (`PublishAudioEffects` target), and ships the
  native `Groovefin.Audio.Effects.dll`, which exports `DllGetActivationFactory`. The target lives in
  `PublishAudioEffects.targets`, which the effect's tests import too. NativeAOT is used instead
  of C#/WinRT's `WinRT.Host.dll` because that host needs an installed .NET runtime, while the unpackaged
  app is self-contained.
- `Package.appxmanifest` (MSIX) and `app.manifest` (unpackaged, registration-free WinRT) register the
  class `Groovefin.Audio.Effects.EqualizerEffect` in that DLL.
- `MediaPlayerEngine` asks Windows for the class's activation factory first, logs the result
  ("Equalizer effect is available"), and only then adds it, as optional. Without it, Settings hides the
  equalizer and playback works as before. The CI smoke test fails if the log says otherwise.
- The effect and the app run different .NET runtimes in one process. The app passes the parameters as
  one string (`EqualizerParameters.Format`) to a function the DLL exports
  (`EqualizerEffectContract.SetParametersExport`, called by `EqualizerEffectLibrary`); every effect in the
  process uses what was set last, from its next buffer on. The effect's property set is not used: Media
  Foundation did not pass later changes to it on to the effect (#169).
- The effect cannot log and never lets an error stop playback. It counts the buffers it received,
  equalized and left alone after an error (`GetStatusExport`, `EqualizerEffectStatus`); the engine logs new
  errors at the start of each track.
- The filter maths lives in `Groovefin.Audio` and is unit-tested on every platform. The effect project
  itself builds only on Windows (`cswinrt.exe`). `Groovefin.Audio.Effects.Tests` runs the built DLL in CI:
  Media Foundation's transcoder creates the effect like MediaPlayer does, but needs no sound card. A boost
  of the 1 kHz band must make a 1 kHz tone about 12 dB louder.

## Jellyfin layer

- `JellyfinClient` owns the active `ServerConnection` (server URL, user ID, access token) and one SDK
  client (`JellyfinApiClient`) per connection. `Connect` replaces it; the focused services always use
  the current one.
- Every request carries `Authorization: MediaBrowser Client="Groovefin", Device=…, DeviceId=…,
  Version=…[, Token=…]`. The SDK's own header drops the client fields once a token is set, so
  Groovefin uses its own `IAuthenticationProvider`. The device ID is stable per Windows user and
  machine (`EasClientDeviceInformation`).
- DTOs are mapped to Core models in `Mapping/`. IDs use Jellyfin's 32-digit hex form.
- SDK (`ApiException`) and transport errors become `JellyfinException` with the HTTP status code
  (`null` when the server was unreachable). Callers never see SDK types.
- The focused interfaces (`IPlaylistService`, `ISearchService`, `ILyricsService`,
  `IPlaybackReporter`) are added with the features that need them (#36, #41, #44, #31).

## Conventions

### Threading

- All I/O is `async` and takes a `CancellationToken`. Nothing blocks the UI thread.
- ViewModel properties are changed on the UI thread. Background work (engine events, network
  callbacks) uses `IDispatcher.EnqueueAsync`.
- Page ViewModels cancel their outstanding loads when the user navigates away. Cached pages
  (`NavigationCacheMode`, e.g. Albums) are the exception: they keep their ViewModel, list and scroll
  position for the next visit.

### Networking and errors

- The Jellyfin layer uses `IHttpClientFactory` with the `Microsoft.Extensions.Http.Resilience`
  standard pipeline: up to 3 retries with exponential back-off and jitter for transient failures
  (network errors, timeouts, 408, 429, 5xx) of **safe** methods only, 15 s per attempt, 45 s per
  request, and a circuit breaker. 4xx such as 401 are never retried.
- Services throw only `JellyfinException` (HTTP status, or `null` when the server was unreachable or
  timed out). Caller cancellation stays an `OperationCanceledException` and is not an error.
- Failures are never silent. Code that catches an exception and does not rethrow calls
  `IErrorReporter.Report(exception, Operations.X)`. It logs the details and shows a localized
  InfoBar in the shell through `INotificationService` (at most 3, duplicates dropped).
- Unhandled XAML and unobserved task exceptions are reported the same way and the app keeps running.

### Logging

- `Microsoft.Extensions.Logging` everywhere, Serilog as the provider.
- Rolling log files in local app data (`Logs\groovefin-.log`), retained for 7 days.
- Never log access tokens, passwords or full authorization headers.

### Settings and secrets

- Settings: JSON in `ApplicationData.LocalFolder` (packaged) or `%LOCALAPPDATA%\Groovefin`
  (unpackaged).
- Access tokens: `Windows.Security.Credentials.PasswordVault`. Passwords are never stored.

### MVVM

- `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`).
- Cross-ViewModel communication via `IMessenger` (e.g. `FavouriteChangedMessage`,
  `NowPlayingChangedMessage`).

### UI

- Theme resources and the system accent colour only. No hard-coded colours or fonts.
- Virtualized lists everywhere (`ItemsView` / `ItemsRepeater`, incremental loading).
- Every user-visible string comes from `.resw` (`en-US`, `de-DE`).
  `ResourceFileTests` (Core tests, runs in CI) fails when the two languages differ in keys or
  `{0}` placeholders, when an `x:Uid` or a key named in code has no string, or when a string is unused.
  Keys are `Area_Name` (code) or `XUid.Property` (XAML). Numbers and dates are formatted with
  `CultureInfo.CurrentCulture`.
- Every custom control sets `AutomationProperties`.
- Access keys (Alt) for the navigation, the search box and the now playing bar come from `.resw`
  (`XUid.AccessKey`), because the letters follow the localized labels. They share the window's
  scope, so a letter is used once per language; `ResourceFileTests` checks it.
- Animations are subtle and native (connected animation for album covers) and are skipped when the
  Windows "Animation effects" setting is off.

### TODOs

Every `TODO` references a GitHub issue: `// TODO(#42): handle transcoding fallback`.
