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
| `build-windows (x64)`, `build-windows (ARM64)` | Windows | Release build of the full solution for each platform, then packages `Groovefin.App`: an unsigned MSIX (`groovefin-msix-<platform>`) and an unpackaged, self-contained build (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, artifact `groovefin-unpackaged-<platform>`). Artifacts are kept for 7 days so every PR has a testable build. |

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

To release, push a tag: `git tag v0.1.0 && git push origin v0.1.0` (see `release.yml`, #56).

## Variables and secrets

| Name | Kind | Used by | Purpose |
|---|---|---|---|
| `WINDOWS_RUNNER` | Repository variable (optional) | Windows jobs | Runner label override. Default `windows-2025`. |
| `SIGNING_CERT_PFX_BASE64` | Repository secret | `release.yml` | Base64-encoded PFX for MSIX signing. |
| `SIGNING_CERT_PASSWORD` | Repository secret | `release.yml` | Password of the PFX. |

The Jellyfin test variables (`JELLYFIN_TEST_URL`, `JELLYFIN_TEST_USER`, `JELLYFIN_TEST_PASSWORD`) are
for local integration testing only. They are never used in CI.
