# CI/CD

All automation runs on GitHub Actions. This page describes each workflow, its jobs, and the
variables and secrets it uses.

## Principles

- **Least privilege.** Every workflow sets `permissions: contents: read` at the top. A job gets more
  only when it needs it, and declares it on the job.
- **Pinned actions.** Third-party actions are pinned to a full commit SHA with a version comment
  (`uses: actions/checkout@<sha> # v7.0.1`). Dependabot keeps the SHAs current.
- **Superseded runs are cancelled.** PR runs share a `concurrency` group per PR. A new push cancels
  the old run. Runs on `main` are never cancelled.
- **Reproducible restores.** NuGet lock files (`packages.lock.json`) are committed. On GitHub Actions
  `RestoreLockedMode` is on (see `Directory.Build.props`), so restoring a dependency that is not in
  the lock file fails the build. After changing a package, run `dotnet restore` locally and commit
  the updated lock files.
- **SDK from `global.json`.** `actions/setup-dotnet` reads `global.json`
  (`rollForward: latestFeature`, so CI uses the newest .NET 10 SDK). The NuGet cache key is built
  from `Directory.Packages.props` and all `packages.lock.json` files.

## Runners

| Job type | Runner |
|---|---|
| Format, cross-platform tests | `ubuntu-latest` |
| Windows builds and packaging | `${{ vars.WINDOWS_RUNNER \|\| 'windows-2025' }}` |

To use a self-hosted Windows runner, set the repository variable `WINDOWS_RUNNER` to its label
(Settings → Secrets and variables → Actions → Variables). The workflow files do not change.

## `ci.yml`

Triggers: `pull_request`, `push` to `main`, manual (`workflow_dispatch`).

| Job | Runner | What it does |
|---|---|---|
| `format` | Ubuntu | `dotnet format Groovefin.sln --verify-no-changes`. Fails when code does not match `.editorconfig`. Fix locally with `dotnet format Groovefin.sln`. |
| `test-core` | Ubuntu | Builds and tests `Groovefin.CrossPlatform.slnf` (Core, Jellyfin and their tests) on Microsoft.Testing.Platform. Writes a TRX results table and a Cobertura coverage summary to the job summary, and uploads TRX + coverage as the `test-results` artifact (7 days). |
| `build-windows (x64)`, `build-windows (ARM64)` | Windows | Release build of the full solution for each platform, then packages `Groovefin.App`: an unsigned MSIX (`groovefin-msix-<platform>`) and an unpackaged, self-contained build (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, artifact `groovefin-unpackaged-<platform>`). Artifacts are kept for 7 days so every PR has a testable build. The x64 job then runs `Groovefin.Audio.Effects.Tests.exe`, which sends a tone through the NativeAOT equalizer effect with Media Foundation's transcoder and checks the level with the equalizer off, a band boosted, a band cut and the preamp lowered (#169). After that it runs the smoke test `.github/scripts/Test-AppStarts.ps1` on the unpackaged app. It must log `Start-up took …` (window shown, session restored) within 30 seconds, and `Equalizer effect is available` (Windows can create the NativeAOT audio effect the manifest registers). Then, through UI Automation and against a small fake Jellyfin server on `127.0.0.1:8096`, it adds the server, signs in, opens Home, Folders and Songs (#171), checks that the home page's album tiles have the cover buttons (#189), checks on Albums, Artists and Genres that the tiles grow to fill their rows, so whole columns reach the right margin (#232), opens an album and plays it (the fake server streams a generated WAV, #167), checks that its Last.fm link shows the site's icon before its name (#198), that its track list's column header looks like the Songs page's (a 36 px row, labels in normal case, #216, and a divider between its columns, read from the screen, #220), opens the mini player there and leaves it, checking that the album header ends where it did (#218), then follows the album's artist link to the artist page (#172) and goes back and forward with the mouse's X buttons and Alt+Left (#176). With the mouse it then plays an album (checking that it is reported to the server, and not with the privacy mode button in the title bar on, #234), a genre (#191) and an artist from the buttons on their tiles and a song from an album's songs popup, and checks that none of them also opens the album or artist page (#185), and that the buttons go again once the pointer has left the round artist image, through a corner and after a quick sweep into the gap beside the circle (#187), and that the clicked play button then gives its focus to the tile (#236). With a sound card the position must move and no track may fail. GitHub's runners have none, so there the stream must get past the equalizer effect and fail only at the audio output (`0xC00D36FA`). Either way the log must not show an exception from .NET code inside Media Foundation (`0x8013xxxx`) or the engine taking the effect out, opens Favourites and selects its tabs one after another, checking that each shows its own list (#202), opens Settings, checks that it shows the Discord setting (hidden in a build without the Discord application ID, #160), expands the equalizer and checks that its band sliders draw something (not one flat colour, #165), turns the equalizer on and checks that playback goes on, gives a keyboard shortcut new keys in the change dialog and resets them (#68), opens the mini player with Ctrl+M and checks that its client area opens at 420 × 140 (read with `GetClientRect`: the window's bounds also count the invisible resize borders, #222) and that it resizes up to 720 × 200 at most (#181, #183), shrinks the full window far below its minimum and checks that its client area stops at 860 × 600 and that, with the queue open, every button of the title bar, the now playing bar and the queue is whole inside it, the bar's without overlapping (#241), and closes the window. A screenshot of the settings page is uploaded as `smoke-test-screenshots`; on a manual run (`workflow_dispatch`) it is also printed into the log as base64. On each page it opens (servers, sign-in, Home, Folders, Songs, an album, an artist, Albums, Genres, Artists, Favourites and Settings), and with the queue, the lyrics (each opened and closed with its shortcut, the page as wide beside either, #239), the full-screen player (opened and closed with its shortcut), the shortcut dialog and the mini player open, it runs `tools/Groovefin.AccessibilityScan`, which checks the window with the [Axe.Windows](https://github.com/microsoft/axe-windows) rules, the automated checks of Accessibility Insights FastPass (#51). Each error is printed with the element it was found on; pages with errors also get an `.a11ytest` file in the `accessibility-scans` artifact, which opens in Accessibility Insights for Windows. The test fails at the end if any page had errors. On Home, an album, an artist, Albums, Favourites and Settings it also walks the tab order: Tab from the focused element until focus comes back. Every stop must have a name and a place on screen, Tab must not get stuck, and each part of the window (title bar, search box, privacy mode button, navigation, page, now playing bar; the page and the bar are found by their landmarks) must be one run of stops; the order is printed to the log. Two errors on elements the Windows App SDK and WinUI create are printed as ignored instead: the hidden Minimize and Maximize buttons of the mini player's title bar (no bounds, not marked offscreen) and a popup host's input pane (exactly its container's size). The app cannot change either. It fails if the app exits too early, logs an unhandled exception, or does not exit with code 0 after the window is closed. It prints how long each shutdown step took; a step the app skipped after its time limit becomes a warning, a step that failed fails the test (#230), and if the app is still running 20 seconds after closing, the managed stacks of all its threads are printed (`dotnet-stack`, #206). It then prints `%LOCALAPPDATA%\Groovefin\startup-error.log`, which lists every thrown exception because the test sets `GROOVEFIN_TRACE_EXCEPTIONS=1`, plus the app log, the fake server's requests and the Windows error events. |

The unpackaged publish first deletes the App project's `bin` and `obj` folders for the platform. The
MSIX build leaves a `resources.pri` without the Windows App SDK's own resources (packages get them
from the runtime package), and an incremental publish would reuse it, so the unpackaged app could not
find its theme resources and crashed on start (#133).

If the Windows build fails, every MSBuild error (including XAML compiler `WMC…` errors, which the
default problem matcher misses) is also reported as an annotation on the check run, so the cause is
visible in the PR's *Checks* tab and through the API without opening the raw log.

The status check names `format`, `test-core`, `build-windows (x64)` and `build-windows (ARM64)` are
the required checks for `main` (see [contributing.md](contributing.md#branch-protection-for-main)).

### Running the same checks locally

```sh
dotnet format Groovefin.sln --verify-no-changes
dotnet test --solution Groovefin.CrossPlatform.slnf -c Release \
  --report-xunit-trx --coverage --coverage-output-format cobertura \
  --coverage-settings "$PWD/tests/coverage.config"
```

`--coverage-settings` needs an absolute path, because each test application resolves it from its
own working directory.

The accessibility scan runs on Windows against a running Groovefin (any build), with its process id:

```powershell
dotnet run --project tools/Groovefin.AccessibilityScan -- (Get-Process Groovefin).Id home $env:TEMP\a11y
```

## `codeql.yml`

Triggers: `pull_request`, `push` to `main`, weekly (Monday 03:27 UTC), manual.

CodeQL analyses C# with the `security-and-quality` query suite. It uses `build-mode: manual` and
traces a Release x64 build of the full solution on the Windows runner, so the WinUI project is
analysed too. Results appear under Security → Code scanning. A PR must not add new alerts.

The job needs `security-events: write` to upload results; everything else is read-only.

## `release.yml`

Triggers: a pushed tag `v*.*.*` (`v1.2.0`, `v1.3.0-beta.1`). Manual runs (`workflow_dispatch`) and
pull requests that change `release.yml` or `.github/scripts/*.ps1` are dry runs: they build, sign
and checksum everything as artifacts but publish no release (pull requests also skip the
attestations).

| Job | Runner | What it does |
|---|---|---|
| `build (x64)`, `build (ARM64)` | Windows | Builds the MSIX and the unpackaged, self-contained app for the platform, the same way as CI. Zips the unpackaged app and signs the MSIX (if the certificate is set). Artifact `release-<platform>`. |
| `bundle` | Windows | Combines both MSIX into one `.msixbundle` with `makeappx` and signs it. Artifact `release-bundle`. |
| `publish` | Ubuntu | Writes `SHA256SUMS.txt`, creates build provenance attestations for every package, and (tag only) runs `gh release create --generate-notes`. A tag with a pre-release part (`-beta.1`) becomes a pre-release. |

Release files for `v1.2.0`:

| File | What |
|---|---|
| `Groovefin_1.2.0.msixbundle` | Installer for both architectures (recommended) |
| `Groovefin_1.2.0_x64.msix`, `Groovefin_1.2.0_ARM64.msix` | Installer for one architecture |
| `Groovefin_1.2.0_x64_unpackaged.zip`, `Groovefin_1.2.0_ARM64_unpackaged.zip` | Portable app; extract and run `Groovefin.exe` |
| `SHA256SUMS.txt` | Checksums of all files |

The MSIX packages need the Windows App Runtime (a framework package from Microsoft) on the PC. The
unpackaged ZIPs are self-contained and need nothing installed.

Verify a download with `gh attestation verify <file> --repo sysmike/groovefin`.

**Signing.** The jobs decode `SIGNING_CERT_PFX_BASE64` to a file in the runner's temp folder, sign
with `.github/scripts/Sign-Package.ps1` (signtool, SHA-256, RFC 3161 timestamp) and delete the file
in an `always()` step. Without the secret the workflow still runs: it publishes unsigned packages,
adds a warning annotation, and the release notes say that the packages are unsigned.
`Sign-Package.ps1` is the only place that knows how to sign, so moving to Azure Artifact Signing
means replacing that script and the certificate steps.

The certificate subject must be the Publisher in `Package.appxmanifest` (`CN=Groovefin`). A
self-signed certificate for testing (users must trust it before they can install):

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Groovefin' `
  -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(3)
$password = Read-Host -AsSecureString 'PFX password'
Export-PfxCertificate -Cert $cert -FilePath groovefin.pfx -Password $password
[Convert]::ToBase64String([IO.File]::ReadAllBytes('groovefin.pfx')) | Set-Clipboard
```

Paste the clipboard into the `SIGNING_CERT_PFX_BASE64` secret and the password into
`SIGNING_CERT_PASSWORD`. Then delete `groovefin.pfx`.

## Dependabot

`.github/dependabot.yml` checks every Monday:

| Ecosystem | What | Grouping | Labels |
|---|---|---|---|
| `nuget` | `Directory.Packages.props`, lock files, `.config/dotnet-tools.json` | minor + patch in one PR (`nuget-minor-patch`); majors separately | `dependencies`, `type:chore` |
| `github-actions` | Pinned action SHAs in `.github/workflows` | minor + patch in one PR (`actions-minor-patch`) | `dependencies`, `area:ci` |

Dependabot PRs run the normal CI. Merge them like any other PR once green.
### Testing a PR build

- **Unpackaged:** download `groovefin-unpackaged-<platform>`, extract it and run `Groovefin.exe`.
- **MSIX:** the CI package is unsigned. Install it in a PowerShell window started as administrator
  with Developer Mode on: `Add-AppxPackage -AllowUnsigned .\Groovefin_*.msix`.

## Versioning

Versions come from git tags via [MinVer](https://github.com/adamralph/minver) (a global package
reference in `Directory.Packages.props`, tag prefix `v`).

| Situation | Version |
|---|---|
| Commit tagged `v1.2.0` | `1.2.0` |
| Commit tagged `v0.3.0-beta.1` | `0.3.0-beta.1` |
| 5 commits after `v1.2.0` | `1.2.1-alpha.0.5` |
| No tag yet | `0.0.0-alpha.0.<height>` |

- `InformationalVersion` is `<version>+<commit SHA>`. The About page shows the version and the
  short SHA (`AppVersion` in Core).
- `Package.appxmanifest` keeps `0.0.0.0` in source. At build time `Directory.Build.targets` writes
  a copy to `obj/` with `Major.Minor.Patch.0` (MSIX versions are numeric) and packages that copy.
- MinVer needs the full history. Every CI checkout uses `fetch-depth: 0`. In a shallow clone the
  version falls back to `0.0.0-alpha.0`.

To release, push a tag: `git tag v0.1.0 && git push origin v0.1.0` (see [`release.yml`](#releaseyml)).

## Variables and secrets

| Name | Kind | Used by | Purpose |
|---|---|---|---|
| `WINDOWS_RUNNER` | Repository variable (optional) | Windows jobs | Runner label override. Default `windows-2025`. |
| `SIGNING_CERT_PFX_BASE64` | Repository secret | `release.yml` | Base64-encoded PFX for MSIX signing. |
| `SIGNING_CERT_PASSWORD` | Repository secret | `release.yml` | Password of the PFX. |

The Jellyfin test variables (`JELLYFIN_TEST_URL`, `JELLYFIN_TEST_USER`, `JELLYFIN_TEST_PASSWORD`) are
for local integration testing only. They are never used in CI.
