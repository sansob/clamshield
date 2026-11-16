# Development guide

## What you need

- The [.NET 10 SDK](https://dotnet.microsoft.com/download).
- ClamAV, if you want to scan anything. Building and most tests work without it. On Linux, `sudo dnf install clamav clamd clamav-update` or `sudo apt install clamav clamav-daemon`.
- Python 3 and ImageMagick are only needed for `scripts/screenshots.sh`.

## Build, run, test

```sh
dotnet build                          # the whole solution
dotnet run --project src/Clamshield   # start the app
dotnet test                           # run the unit tests
```

Open `Clamshield.sln` in Rider, Visual Studio or VS Code with the C# extension. Avalonia's previewer works with the XAML files.

A debug build starts the Avalonia developer tools. Press F12 in the running window.

## Run against throwaway data

The app reads and writes its settings, history and quarantine in the data folder. While developing you rarely want your real one. On Linux and macOS the folder follows `XDG_CONFIG_HOME`, so this runs a completely separate copy:

```sh
XDG_CONFIG_HOME=/tmp/clamshield-dev dotnet run --project src/Clamshield
```

Put `{"SetupDone": true}` in `/tmp/clamshield-dev/Clamshield/settings.json` to skip the setup wizard.

Only one copy of the app runs at a time. If a second one starts, it hands its request to the first and exits. When you have a copy open and the one you just built seems to do nothing, that is why.

## Environment variables

| Variable | Effect |
|---|---|
| `XDG_CONFIG_HOME` | Moves the data folder (Linux and macOS) |
| `CLAMAV_DIR` | An extra folder where the ClamAV programs are looked for |
| `CLAMSHIELD_SHOTS=<folder>` | Render every screen to a PNG in that folder, then quit |
| `CLAMSHIELD_SIZE=1280x800` | Window size for the screenshot mode |
| `CLAMSHIELD_THEMES=dark` or `light` | Which theme the screenshot mode renders. Both when unset |
| `CLAMSHIELD_SCANDIR=<folder>` | A folder to scan during the screenshot mode, for the scan result screens |

## Testing the scanner safely

The EICAR string is a harmless text file that every antivirus agrees to flag. It is the safe way to test detection:

```sh
printf '%s' 'X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*' > /tmp/eicar.txt
dotnet run --project src/Clamshield -- --cli --scan /tmp --json
```

The command line mode needs no window, so it is also the quickest way to try a change to the scan code.

## Unit tests

Tests live in `tests/Clamshield.Tests` and use xUnit. Before any service is used, `TestEnvironment` points the data folder and the home folder at a fresh temporary directory, so tests never touch your real data.

Some tests need the real ClamAV and return early when it is not installed. They are not marked as skipped, so a run on a machine without ClamAV is still green. CI installs ClamAV on the Linux job so they run there.

Two tests guard the translations. They fail when the interface asks for a translation key that does not exist, or when code passes an English text to the translator that has no Indonesian version. Run the tests after you add any text.

## Screenshots for the docs

```sh
scripts/screenshots.sh
```

It builds the app, creates made-up demo data in a throwaway folder, renders every screen in both themes and copies the chosen ones into `docs/screenshots`. Your real settings are not used. Run it after you change the look of a screen, and commit the new images with the change.

## Adding a setting

1. Add a property to `AppSettings` in `Models/Models.cs`, with a default.
2. Add a property with the same name to `MainViewModel.Features.cs`. Its setter should update `S`, call `Save()`, raise `PropertyChanged` for itself, and apply the change if it has an effect right away.
3. Add a row in `MainWindow.axaml` in the right Settings tab. Copy a neighbouring `Border Classes="row"`.
4. Add the texts to `LocStrings.cs`. See [TRANSLATING.md](TRANSLATING.md).
5. If a service needs the value, expose it through a small static class like `ScanOptions` rather than reading the settings file from the service.

## Adding a tool page

1. Add a number to the `ToolView` property and a matching `IsTool...` flag in `MainViewModel.Features.cs`.
2. Add a tile in the tool picker and a panel for the page in `MainWindow.axaml`, bound to that flag.
3. Put the real work in a service under `Services/` and keep the view model code to calling it and showing the result.

## Conventions

- Nullable reference types are on. Do not silence a warning without a reason.
- Services never throw to the UI. They return an error message or an empty result, because a failed scan must not crash the app.
- Start external programs with `ProcessStartInfo.ArgumentList`, never with a command string.
- Anything that can take longer than a blink runs off the UI thread.
- Visible text goes through the translator. No bare strings in XAML or in code that the user can read.
- Colours come from the theme resources in `App.axaml` (`TextMain`, `BgCard`, ...) so both themes keep working. Accent colours that look the same in both themes can be written directly.
- Icons are wrapped in a `Viewbox` of a fixed size so they stay centred.

## Troubleshooting

### The app starts and closes at once, or a second copy does nothing

Another copy is already running, see above.

### Scans take several seconds to start

The private daemon is still loading the database. The first scan after the app starts falls back to `clamscan`, which loads the database each time.

### "ClamAV not found" in the wizard on Linux

The wizard installs it through `pkexec`. A system without a graphical polkit agent cannot show the password prompt. Install ClamAV yourself and restart.

### Wrong or missing icon in the Linux dock

The shell matches the window to `~/.local/share/applications/clamshield.desktop` through the window class `clamshield`. The app writes that file on start. Delete it and start the app again if it is stale.
