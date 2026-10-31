# Groovefin

A native [Jellyfin](https://jellyfin.org) music player for Windows 11, built with C#, .NET 10 and
WinUI 3.

[![CI](https://github.com/sysmike/groovefin/actions/workflows/ci.yml/badge.svg)](https://github.com/sysmike/groovefin/actions/workflows/ci.yml)
[![CodeQL](https://github.com/sysmike/groovefin/actions/workflows/codeql.yml/badge.svg)](https://github.com/sysmike/groovefin/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Features

**Library**
- Home with shelves of recently added, recently played, most played, random and favourite albums.
- Albums, artists, genres, playlists and folders, in a grid or a list, with sorting and filters.
- A songs table with sortable, resizable and movable columns.
- Global search with live suggestions, grouped by artists, albums, tracks and playlists.
- Favourites, playlists (create, rename, reorder, add by drag and drop; favourite playlists in the navigation pane, in your own order) and instant mixes.

**Playback**
- Gapless playback, volume normalization from the server's loudness data (track or album), and
  stream quality limits for the home network and remote servers.
- A 10-band equalizer with presets and a preamp, heard as you move the sliders.
- Optional crossfade of up to 12 seconds between tracks; tracks of the same album stay gapless.
- A queue panel with drag and drop, shuffle and repeat.
- Synced lyrics that follow the song (LRC), with click-to-seek and an offset.
- Resume where you left off: the queue and position come back after a restart.

**Windows integration**
- Media keys and the Windows media flyout.
- Taskbar thumbnail buttons and playback progress on the taskbar button.
- An optional icon in the notification area, with playback controls.
- A full-screen player with a blurred cover backdrop, and an always-on-top mini player.
- Keyboard shortcuts for everything you do often, listed in Settings, where you can give each one other keys.
  Settings is the gear button in the title bar, or Ctrl+,.
- The mouse's back and forward buttons (or Alt+Left and Alt+Right) move through the pages you visited.
- An optional Discord status ("Listening to <artist>") with title, artist and progress, through
  the Discord app on the PC: no keys, and nothing goes to the internet from Groovefin. The artist
  links to Last.fm, the album to MusicBrainz when your server knows its ID.
- A privacy mode (the button beside Settings in the title bar, Ctrl+Shift+P or the tray menu): no plays are
  reported to your server and Discord shows nothing, until you turn it off or restart Groovefin.

**Look and feel**
- Native WinUI 3 with Mica: light, dark and high contrast, the Windows accent colour (or neutral
  grey), and a compact density.
- Works with the keyboard and Narrator.
- English and German.

## Screenshots

_Screenshots in light and dark follow with the first release (#57)._

## Install

Download the latest release from the [Releases page](https://github.com/sysmike/groovefin/releases).

| File | For |
|---|---|
| `Groovefin_<version>.msixbundle` | Most people: installs the right version for your PC (x64 or ARM64). |
| `Groovefin_<version>_x64.msix`, `…_ARM64.msix` | One architecture only. |
| `Groovefin_<version>_<arch>_unpackaged.zip` | Portable: extract anywhere and run `Groovefin.exe`. Nothing to install. On start it adds itself to your Start menu, so Windows can show its name in the media controls. |

- **Requirements:** Windows 11 (version 21H2 or later) on x64 or ARM64, and a
  [Jellyfin](https://jellyfin.org) server. Lyrics need Jellyfin 10.9 or later.
- The MSIX packages need the Windows App Runtime from Microsoft. If the installer reports that it
  is missing, use the portable ZIP, which brings everything it needs.
- If a release is marked as unsigned, install the MSIX with Developer Mode on
  (Settings → System → For developers) and `Add-AppxPackage -AllowUnsigned .\Groovefin_*.msix`, or
  use the ZIP.
- Check a download with its line in `SHA256SUMS.txt`, or with
  `gh attestation verify <file> --repo sysmike/groovefin`.

On first start, enter your server address (for example `https://192.168.1.10:8920`), then sign in
with your Jellyfin user, or with Quick Connect. Use the server's `https://` address if it has one:
over plain `http://`, your password and access token cross the network unencrypted, and Groovefin
asks before it sends them.

## Build from source

You need Windows 11 and either Visual Studio 2026 (workloads *.NET desktop development*, *WinUI
application development* and *Desktop development with C++*) or the .NET 10 SDK with the Visual C++
build tools. The C++ tools link the equalizer's audio effect (NativeAOT); without them, build with
`-p:GroovefinAudioEffects=false` and the app runs without the equalizer.

```powershell
git clone https://github.com/sysmike/groovefin.git
cd groovefin
dotnet build Groovefin.sln -c Release -p:Platform=x64
dotnet test --solution Groovefin.CrossPlatform.slnf -c Release
```

To run the app, open `Groovefin.sln` in Visual Studio, set `Groovefin.App` as the startup project
and press F5. Use `-p:Platform=ARM64` on ARM PCs.

The core libraries and their tests also build on Linux and macOS
(`dotnet test --solution Groovefin.CrossPlatform.slnf`).

- [Architecture](docs/architecture.md): projects, layers and conventions.
- [Contributing](docs/contributing.md): issues, branches, commits and the definition of done.
- [CI/CD](docs/ci.md): workflows, releases and signing.

## License

Groovefin is licensed under the [MIT License](LICENSE). The licenses of the components it uses are
listed in the app (Settings → About → Third-party software).

Groovefin is not affiliated with the Jellyfin project. [Feishin](https://github.com/jeffvli/feishin)
(GPL-3.0) is used as a UX reference only; no code or assets are copied from it.
