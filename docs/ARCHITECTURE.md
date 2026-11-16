# Architecture

Clamshield is a single-window desktop app written in C# on .NET 10 with the [Avalonia](https://avaloniaui.net/) UI toolkit. It does not link against ClamAV. It starts ClamAV's own programs (`clamscan`, `clamdscan`, `clamd`, `freshclam`, `sigtool`) as separate processes and reads their output.

```
  +------------------------+      +-------------------------------+
  |  MainWindow (XAML)     |      |  Services (static classes)    |
  |  code-behind handlers  | ---> |  ScanService, RealTimeService |
  +-----------+------------+      |  QuarantineService, ...       |
              |                   +---------------+---------------+
   bindings   |                                   |
              v                                   | starts and reads
  +------------------------+                      v
  |  MainViewModel         |      +-------------------------------+
  |  (3 partial files)     |      |  ClamAV programs              |
  +------------------------+      |  clamd  clamdscan  clamscan   |
                                  |  freshclam  sigtool           |
                                  +-------------------------------+
```

## Layers

**Views.** `MainWindow.axaml` holds every screen. Pages are panels whose `IsVisible` is bound to a view model flag, and a style plays a short fade-and-slide when a page becomes visible. `MainWindow.axaml.cs` contains only click handlers that call the view model. `App.axaml` has the theme colours, icons and control styles.

**View model.** `MainViewModel` is split over three files:

| File | Contains |
|---|---|
| `MainViewModel.cs` | navigation, scanning, quarantine, history, updates, the setup wizard, the dashboard state |
| `MainViewModel.Features.cs` | settings properties, real-time activity, the scheduler, health score, weekly statistics, block list, YARA rules, the file shredder, the out-of-date check, start with the computer and the right-click menu |
| `MainViewModel.Tools.cs` | pausing, link check, password check, startup programs, disk clean-up, the HTML report |

It implements `INotifyPropertyChanged` by hand. There is no MVVM library. Every setting is a property whose setter saves the settings file at once.

**Services.** Static classes in `Services/` that do the real work. They know nothing about the UI, which is why most of them can be tested directly. The view model calls them and marshals results back to the UI thread.

**Models.** `Models/Models.cs` holds the plain data: `AppSettings`, `ScanRecord`, `ThreatItem`, `QuarantineItem` and a few others, serialised as JSON.

## Services

| Service | Job |
|---|---|
| `ClamAvLocator` | Finds the ClamAV programs (PATH, usual install folders, our own engine folder), owns the database folder and writes the private `freshclam.conf` |
| `EngineInstaller` | Installs ClamAV when it is missing: the package manager on Linux, the official release download on Windows and macOS |
| `ClamdService` | Runs a private `clamd` with its own config and keeps its state |
| `ScanService` | Collects files, runs the scan, parses the output, runs `freshclam` |
| `RealTimeService` | File watchers, the queue and batched scanning |
| `QuarantineService`, `TrustedFiles`, `Blocklist`, `YaraRules` | Quarantine storage, the trusted list, the block list database, validated rules |
| `RansomwareShield`, `PersistenceWatch`, `DriveWatcher` | The three watchers described in [FEATURES.md](FEATURES.md) |
| `VirusTotalService`, `VirusTotalUrl`, `PasswordBreach` | The only code that talks to external web services |
| `Shredder`, `CleanupService`, `StartupItems`, `RunningPrograms`, `EngineDetails` | The tools |
| `AutoStart`, `ShellIntegration`, `LinuxIntegration`, `NotificationService`, `SingleInstance` | Operating system integration |
| `PowerState`, `EmailFolders` | Battery and full-screen detection, mail client folders |
| `Storage` (`AppData`, `SettingsService`, `HistoryService`) | The data folder, JSON files and permissions |
| `Loc`, `LocStrings` | Translations |
| `Cli` | The command line mode |

## How a scan works

1. `ScanService.RunAsync` walks the chosen folders without following links, drops excluded files and media types (if skipped) and writes the remaining paths to a temporary list file. The count gives the progress percentage.
2. If `ClamdService.Ready` is true it runs `clamdscan --multiscan --file-list=...` against the private daemon. Otherwise it runs `clamscan` with the same list, pointing `--database` at our own database folder when we have one.
3. It reads stdout line by line. Each line is `path: result`, and the result `... FOUND` is a threat. The path is split at the last `: ` because paths can contain colons.
4. If the daemon call fails with nothing scanned, the same list is retried with `clamscan`.
5. Threats that match the trusted list are dropped. The rest go to the view model, which shows them and, in real-time mode, quarantines them.

All external programs are started with an argument list. Nothing is built into a shell string, so file names cannot inject commands.

## The private daemon and database

The system ClamAV setup is usually owned by root: `freshclam` cannot be run by a normal user, and the system database folder is not writable. Clamshield works around this by keeping its own copy:

- `freshclam` runs with a generated `freshclam.conf` that points at a database folder inside the data folder.
- Once that folder holds a complete database, scans use `--database=<our folder>`, and `clamd` gets `DatabaseDirectory <our folder>` in its generated `clamd.conf`.
- `clamd` listens on a Unix socket in the data folder (a loopback TCP port on Windows), runs at below-normal priority and uses half of the processor cores, between 2 and 8 threads.
- Our own extras (block list, YARA rules, community signatures) are plain files in the same folder, so ClamAV loads them like any other database. They only take effect when we use our own database, which is why those features show a hint before the first update.

Changing a scan option that the daemon reads (strict mode, archive handling, the size limit) rewrites `clamd.conf` and restarts the daemon.

## Data on disk

Everything lives in one folder, see [INSTALL.md](INSTALL.md) for its location. The folder and its JSON files are created readable by the owner only on Linux and macOS.

| Path | Content |
|---|---|
| `settings.json` | All settings. Unknown keys are ignored, so older and newer versions can share a file |
| `history.json` | The latest 200 scan records |
| `quarantine/` | `index.json` and the quarantined files as `<id>.bin` |
| `blocklist.json`, `trusted.json`, `persistence.json` | The block list, trusted hashes, the startup baseline |
| `db/` | The virus database and our additions |
| `logs/realtime.log` | The real-time log |
| `reports/` | Weekly reports |
| `clamd.conf`, `freshclam.conf`, `clamd.sock` | Generated configuration and the daemon socket |

## Threading

Scans, real-time batches and every external process run on thread-pool threads. Results are posted to the UI thread with `Dispatcher.UIThread.Post`. A few `DispatcherTimer`s drive the live counters, the scheduler (every 30 seconds) and the pause timer. Nothing blocks the UI thread, except short local file operations such as writing a settings file.

## Translations

`Loc` is a singleton with an indexer, so XAML can bind `{Binding [key], Source={x:Static loc:Loc.I}}` and update the moment the language changes. Code uses `Loc.T("English text")` and `Loc.F("{0} files", n)`. See [TRANSLATING.md](TRANSLATING.md).

## Design decisions worth knowing

Clamshield does not bind to ClamAV as a library. Running the official programs keeps it independent of ClamAV's release cycle, and keeps their GPL code out of this MIT project.

The services are static classes. They are simple to call and to test, at the cost of some global state, for example the data folder. Tests point the data folder at a throwaway directory before any service is touched, see `tests/Clamshield.Tests/TestEnvironment.cs`.

The app has one window and one view model on purpose. It is small enough that splitting it would add more files than clarity.

Icons are vector paths in `App.axaml`, wrapped in a `Viewbox` so they stay centred at any size. A bare `Path` with a uniform stretch sits in the top-left corner of its box, which makes non-square icons look off-centre.

`MainWindow.Screenshots.cs` renders each screen to a PNG when an environment variable is set. It lets anyone regenerate the documentation images without a screen capture tool.
