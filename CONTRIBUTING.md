# Contributing to Clamshield

Thanks for wanting to help. Clamshield is open source and meant to be changed by whoever finds something to fix or improve. You do not need to ask first for small things.

## Where help is most welcome

- Testing on Windows and macOS. The code for those systems is built and unit tested by CI, but it has had little real use. Bug reports with the exact steps are very valuable.
- Translations. See [docs/TRANSLATING.md](docs/TRANSLATING.md). A third language needs a small refactor of the translator, and that is a good first project.
- Packaging. `.deb`, `.rpm`, AppImage, Flatpak, Homebrew, winget, Scoop.
- Accessibility. Screen reader names, keyboard navigation and contrast.
- Narrow windows. The sidebar could collapse to icons.
- Bug fixes and tests. Always.

If you have a bigger idea, open an issue first so we can talk about it before you spend a weekend on it.

## Getting set up

```sh
git clone https://github.com/sansob/clamshield.git
cd clamshield
git switch dev
dotnet run --project src/Clamshield
dotnet test
```

[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) explains how to run against throwaway data, how the screenshots are made and where things live. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) is a tour of the code.

## Making a change

1. Fork the repository and start a branch from `dev`, named for what it does: `fix/quarantine-restore-path`, `feature/slovak-translation`.
2. Make the change. Keep it focused, one thing per pull request is easier to review.
3. Run `dotnet test`. Add a test when you fix a bug or add logic that can be tested without a window.
4. If you changed how a screen looks, run `scripts/screenshots.sh` and include the new images.
5. If people will notice the change, add a line under `## [Unreleased]` in `CHANGELOG.md`.
6. Open a pull request **to `dev`**, not to `main`. Explain what changed and why, and how you tested it.

Pull requests are checked by CI on Linux, Windows and macOS. A change that only works on one of them will usually fail there.

## Commit messages

Write what the commit does, in the imperative, in a short first line. Say why in the body when it is not obvious.

```
Skip symbolic links when counting files for a scan

A link to a folder above the scan root made the file count and the
progress bar run past 100%.
```

## Code style

- Follow the existing style. `.editorconfig` has the basics.
- Keep services free of UI code, and keep the view model free of process and file handling where a service can do it.
- Everything the user can read goes through the translator.
- Be careful with anything that deletes, moves or overwrites files. Those paths need a test and a second look.

The longer list of conventions is in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Reporting bugs

Use the bug report form when you open an issue. The things that help most: your operating system and version, the Clamshield version (Settings, About), what you did, what you expected and what happened. A screenshot is welcome. Please do not paste private paths or your VirusTotal key.

Security problems go through [SECURITY.md](SECURITY.md), not the public tracker.

## Review

A maintainer will look at your pull request when they can. This is a spare-time project, so it may take a few days. Feedback is about the code, never about you, and it is fine to disagree. If a pull request goes quiet for a while, a friendly nudge is welcome.

By contributing you agree that your work is released under the [MIT license](LICENSE) of the project.

Everyone taking part is expected to follow the [code of conduct](CODE_OF_CONDUCT.md).
