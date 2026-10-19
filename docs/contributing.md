# Contributing to Groovefin

Thanks for helping. This page describes how work is planned, how branches and commits are named,
and what a pull request needs before it can be merged.

## Prerequisites

- Windows 11 with Visual Studio 2026 (workloads: *.NET desktop development* and *WinUI application
  development*), or the .NET 10 SDK plus the Windows App SDK build tools.
- The SDK version is pinned in [`global.json`](../global.json).
- `Groovefin.Core`, `Groovefin.Jellyfin` and their tests also build on Linux and macOS:

  ```sh
  dotnet test tests/Groovefin.Core.Tests
  dotnet test tests/Groovefin.Jellyfin.Tests
  ```

See [architecture.md](architecture.md) for the solution layout and [ci.md](ci.md) for the build
pipeline.

## Planning with GitHub Issues

- Every change starts with an issue. Bugs, features, follow-ups and ideas found while working get a
  new issue immediately.
- Issues belong to a milestone (`M0 – Foundation` … `M5 – Polish & 1.0`). Each milestone has a
  tracking issue with its child issues as sub-issues.
- Out-of-scope ideas for 1.0 are labelled `later` and have no milestone.
- Labels:
  - `type:*` — `feature`, `bug`, `chore`, `docs`
  - `area:*` — `ui`, `playback`, `api`, `queue`, `lyrics`, `ci`, `a11y`, `i18n`
  - `priority:*` — `p1` (must have for the milestone), `p2`, `p3`
- A milestone starts only when `main` is green and all `priority:p1` issues of the previous
  milestone are closed.

### Project board

The `Groovefin` project board has the columns **Todo → In progress → In review → Done**.

1. Pick the next issue: `gh issue list --milestone "<current milestone>" --state open`.
2. Assign it to yourself and move it to **In progress**.
3. Open a PR → move it to **In review**.
4. Merging the PR closes the issue → **Done**.

## TODO policy

There are no orphan TODOs. Every `TODO` references an issue:

```csharp
// TODO(#42): handle transcoding fallback
```

If no issue exists, create one first.

## Branches

`main` is protected. Work on a branch per issue:

| Kind | Pattern | Example |
|---|---|---|
| Feature | `feat/<issue>-<slug>` | `feat/21-albums-page` |
| Fix | `fix/<issue>-<slug>` | `fix/88-seek-crash` |
| Other (docs, ci, chore) | `<type>/<issue>-<slug>` | `ci/56-release-workflow` |

## Commits

[Conventional Commits](https://www.conventionalcommits.org/), each referencing an issue:

```text
feat: add album grid with incremental loading

Refs #21
```

Types: `feat`, `fix`, `ci`, `refactor`, `docs`, `test`, `chore`. Code, comments, docs, commits,
PRs and issues are written in English.

## Pull requests

- One PR per issue, with `Fixes #N` in the description so the merge closes the issue.
- Fill in the [PR template](../.github/pull_request_template.md) and its definition-of-done
  checklist.
- Merge only when all required checks are green. PRs are **squash merged**; the PR title becomes
  the commit message, so it must be a Conventional Commit title.
- Label the PR (`type:*`) so it lands in the right release-notes category
  (see [`.github/release.yml`](../.github/release.yml)). Use `skip-changelog` to exclude it.

## Definition of done

- All required checks green (format, tests, x64 + ARM64 Windows build, CodeQL without new alerts).
- New logic in Core / Jellyfin has unit tests.
- UI works in light, dark and high contrast, with keyboard only and with Narrator.
- All user-visible strings are in `.resw`.
- No unhandled exceptions; errors are surfaced via InfoBar and logged.
- Issue acceptance checklist fully ticked; PR merged with `Fixes #N`.

## Branch protection for `main`

The repository owner applies these settings (Settings → Rules → Rulesets, or `gh api`):

| Setting | Value |
|---|---|
| Require a pull request before merging | on (0 required approvals while there is a single maintainer) |
| Require status checks to pass | `format`, `test-core`, `build-windows (x64)`, `build-windows (ARM64)` |
| Require branches to be up to date before merging | on |
| Require linear history | on |
| Allowed merge methods | squash only |
| Block force pushes / deletions | on |

Equivalent `gh api` call (run by the owner):

```sh
gh api -X PUT repos/sysmike/groovefin/branches/main/protection --input - <<'EOF'
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["format", "test-core", "build-windows (x64)", "build-windows (ARM64)"]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": { "required_approving_review_count": 0 },
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false
}
EOF
gh api -X PATCH repos/sysmike/groovefin \
  -F allow_squash_merge=true -F allow_merge_commit=false -F allow_rebase_merge=false \
  -F delete_branch_on_merge=true
```
