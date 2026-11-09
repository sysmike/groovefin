# Groovefin

A music player for Windows 11 that plays the music on your [Jellyfin](https://jellyfin.org) server.
It looks and works like a Windows app, plays without gaps and can keep your favourites on your PC
for when you are offline.

[![CI](https://github.com/sysmike/groovefin/actions/workflows/ci.yml/badge.svg)](https://github.com/sysmike/groovefin/actions/workflows/ci.yml)
[![CodeQL](https://github.com/sysmike/groovefin/actions/workflows/codeql.yml/badge.svg)](https://github.com/sysmike/groovefin/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

![Groovefin's start page: a random album on a colour taken from its cover, then rows of recently played and recently added albums](assets/screenshots/home.webp)

## Features

**Your library**
- A start page with a random album and your recently played and recently added albums.
- Albums, artists, genres, playlists and folders, with sorting and filters.
- All your songs in one table, with the columns you choose.
- Search with suggestions as you type.
- Favourites, playlists and instant mixes.

**Playback**
- Gapless playback, or a crossfade between songs if you like.
- Even volume across songs and albums.
- Lower stream quality for slow connections.
- A 10-band equalizer with presets.
- A queue you can reorder, shuffle and repeat.
- Synced lyrics: click a line to jump to it.
- Your queue and position come back after a restart.
- Downloads for offline listening, kept in sync with your favourites and playlists.

**Windows integration**
- Media keys, the Windows media controls and taskbar buttons.
- A full-screen player and an always-on-top mini player.
- Keyboard shortcuts for everything you do often, which you can change.
- Optional: an icon in the notification area, and your music as your Discord status.
- A privacy mode that stops reporting what you play.

**Look and feel**
- Light, dark and high contrast, in your Windows accent colour.
- Works with the keyboard and Narrator.
- In English and German.

## Install

You need Windows 11 (21H2 or later) and a [Jellyfin](https://jellyfin.org) server. Lyrics need
Jellyfin 10.9 or later.

Download the latest release from the [Releases page](https://github.com/sysmike/groovefin/releases):

| File | Choose it if |
|---|---|
| `Groovefin_<version>.msixbundle` | You are not sure. It installs the right version for your PC. |
| `Groovefin_<version>_x64.msix`, `…_ARM64.msix` | You want the version for one kind of PC only. |
| `Groovefin_<version>_<arch>_unpackaged.zip` | You do not want to install anything. Extract it anywhere and run `Groovefin.exe`. |

If the installer says the Windows App Runtime is missing, use the ZIP instead: it brings everything
it needs.

<details>
<summary>Unsigned releases and checking a download</summary>

- If a release is marked as unsigned, turn on Developer Mode (Settings → System → For developers) and
  install it in PowerShell with `Add-AppxPackage -AllowUnsigned .\Groovefin_*.msix`, or use the ZIP.
- Check a download against its line in `SHA256SUMS.txt`, or with
  `gh attestation verify <file> --repo sysmike/groovefin`.

</details>

## Getting started

1. Enter your server's address, for example `https://192.168.1.10:8920`.
2. Sign in with your Jellyfin user, or with Quick Connect.

Use your server's `https://` address if it has one. Over plain `http://`, your password is sent
unencrypted, and Groovefin asks you first.

Settings is the gear button in the title bar (or Ctrl+,). There you can turn on downloads, the
equalizer and Discord, and change keyboard shortcuts.

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

Groovefin is built with C#, .NET 10 and WinUI 3. The core libraries and their tests also build on
Linux and macOS (`dotnet test --solution Groovefin.CrossPlatform.slnf`).

- [Architecture](docs/architecture.md): projects, layers and conventions.
- [Contributing](docs/contributing.md): issues, branches, commits and the definition of done.
- [CI/CD](docs/ci.md): workflows, releases and signing.

## License

Groovefin is licensed under the [MIT License](LICENSE). The licenses of the components it uses are
listed in the app (Settings → About → Third-party software).

Groovefin is not affiliated with the Jellyfin project. [Feishin](https://github.com/jeffvli/feishin)
(GPL-3.0) is used as a UX reference only; no code or assets are copied from it.
