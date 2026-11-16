# Branches and releases

## Branches

| Branch | Purpose |
|---|---|
| `main` | What has been released. Every commit here that raises the version becomes a release |
| `dev` | Where work comes together. Pull requests go here |
| `feature/...`, `fix/...` | Short-lived branches for one change each, started from `dev` |

```
feature/x ──┐
fix/y ──────┼──► dev ──(release pull request)──► main ──► GitHub release
feature/z ──┘
```

Nobody pushes straight to `main`. `main` only moves when a release pull request from `dev` is merged.

## Continuous integration

`.github/workflows/ci.yml` runs on every push and pull request to `dev` and `main`. It builds and tests on Linux, Windows and macOS, and checks that a self-contained Linux build can be published and starts. A pull request should be green on all three before it is merged.

## Making a release

1. Make sure `dev` is green and that `CHANGELOG.md` has what you want to say under `## [Unreleased]`.
2. In GitHub, open **Actions**, choose **Prepare release** and run it with the new version, for example `1.1.0`.
   It changes `<Version>` in `src/Clamshield/Clamshield.csproj`, turns the `Unreleased` heading of the changelog into the new version with today's date, commits that to `dev` and opens a pull request from `dev` to `main`.
3. Review the pull request. Merge it with a merge commit, not a squash, so the history of `dev` stays visible.
4. The merge to `main` starts `.github/workflows/release.yml`:
   - it reads the version from the project file and stops if a tag for it already exists,
   - it runs the tests,
   - it builds six self-contained single-file apps: `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64` and `osx-arm64`,
   - it packages them (a tarball with an installer for Linux, a zip for Windows, a `.dmg` with an app bundle for macOS),
   - it writes `SHA256SUMS.txt`, creates the tag `v<version>` and publishes a GitHub release with the changelog section as its notes.

If you prefer to do step 2 by hand, edit the version and the changelog yourself, commit to `dev` and open the pull request.

### Version numbers

Semantic versioning. A fix is a patch (`1.0.1`), a new feature a minor version (`1.1.0`), a change that breaks something people rely on, such as the command line options or the settings format, a major one (`2.0.0`).

## Hotfixes

For an urgent fix on a released version, branch from `main`, fix it, open a pull request to `main` and bump the patch version in the same pull request. After the release, merge `main` back into `dev` so the fix is not lost.

## Repository settings you need once

These cannot be set from a file, so whoever owns the repository does them in the GitHub settings:

- Actions, General, Workflow permissions: choose "Read and write permissions" and tick "Allow GitHub Actions to create and approve pull requests". The release and prepare-release workflows need both.
- Branch protection for `main` and `dev`: require a pull request, require the CI checks to pass, and block force pushes. For `dev`, allow the Actions bot to push, because Prepare release commits to it.
- Default branch: keep `main`, so visitors see the released README. Pull requests from contributors target `dev`.

## What the builds are and are not

- They are self-contained: .NET is included, and users install nothing first.
- They are not trimmed. Avalonia's bindings use reflection, which trimming breaks.
- They are not code-signed or notarized. macOS apps get an ad-hoc signature, which Apple silicon needs in order to run them at all. Real signing needs a paid certificate for Windows and an Apple Developer account for macOS. If you get one, the signing steps belong in `release.yml` after the packaging step, using repository secrets.
- The Windows and macOS packaging steps can only run on those systems, so they are exercised by the release workflow itself and not on a developer's Linux machine. If a first release fails on one of those jobs, fix the script and run the workflow again with the **Run workflow** button. It will not publish twice, because it checks for the tag.

## Local packaging check

`scripts/package.sh` is the script the release workflow calls. On Linux you can try the same steps by hand:

```sh
dotnet publish src/Clamshield/Clamshield.csproj -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o /tmp/pub
scripts/package.sh linux-x64 1.0.0 /tmp/pub /tmp/dist
```
