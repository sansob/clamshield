# Installing Clamshield

Every release has a download for each system on the [releases page](https://github.com/sansob/clamshield/releases). The files are self-contained, so you do not need to install .NET or anything else first.

Each release also has a `SHA256SUMS.txt`. If you want to check a download, compare its checksum with the line for that file:

```sh
sha256sum Clamshield-1.0.0-linux-x64.tar.gz     # Linux
shasum -a 256 Clamshield-1.0.0-osx-arm64.dmg    # macOS
Get-FileHash .\Clamshield-1.0.0-win-x64.zip     # Windows (PowerShell)
```

The system requirements are the ones of .NET 10. Microsoft keeps the list of supported Windows, macOS and Linux versions in the [.NET 10 documentation](https://learn.microsoft.com/dotnet/core/install/).

## Why you will see a warning

The builds are not signed with a paid code-signing certificate, because the project does not have one. So the first time you open Clamshield, Windows SmartScreen or macOS Gatekeeper will tell you it comes from an unknown developer. That is expected. The source and the build pipeline are public, so you can read what the release is made from, or build it yourself.

## Windows

1. Download `Clamshield-<version>-win-x64.zip` (use `win-arm64` on ARM machines).
2. Unzip it anywhere you like, for example `C:\Program Files\Clamshield` or your Documents folder.
3. Double-click `Clamshield.exe`.
4. If SmartScreen says "Windows protected your PC", click **More info**, then **Run anyway**.

ClamAV is downloaded by the setup wizard into Clamshield's own data folder. No administrator rights are needed.

## macOS

1. Download the `.dmg` that matches your Mac: `osx-arm64` for Apple silicon (M1 and later), `osx-x64` for Intel.
2. Open it and drag Clamshield to Applications.
3. The first time, right-click the app, choose **Open**, then confirm. A normal double-click is blocked for unsigned apps.

If macOS says the app is damaged, it is the download quarantine flag. Clear it with:

```sh
xattr -dr com.apple.quarantine /Applications/Clamshield.app
```

The app is signed ad hoc, which is enough for it to run on Apple silicon but is not the same as a notarized app.

The wizard downloads ClamAV into Clamshield's data folder. If you already use Homebrew, `brew install clamav` also works and Clamshield will find it.

## Linux

1. Download `Clamshield-<version>-linux-x64.tar.gz` (or `linux-arm64`).
2. Extract it and run the installer:

   ```sh
   tar -xzf Clamshield-1.0.0-linux-x64.tar.gz
   cd Clamshield-1.0.0-linux-x64
   ./install.sh
   ```

   This puts the app in `~/.local/opt/clamshield`, adds a `clamshield` command and a launcher entry with the icon. It all stays inside your home folder, so no administrator rights are needed.
3. Start Clamshield from your app menu.

You can also skip the installer and run `./Clamshield` straight from the extracted folder. The app registers its own launcher entry the first time it starts, so the icon still shows up in the dock.

If ClamAV is missing, the setup wizard installs it with your distribution's package manager (`dnf`, `apt`, `zypper` or `pacman`). That is the one step that asks for your password, through the normal graphical prompt.

The window uses X11. On a Wayland desktop it runs through XWayland, which every mainstream desktop includes.

On GNOME there is no system tray by default. Clamshield turns the "keep running in the tray" option off on Linux for that reason, so closing the window really closes the app. Install the AppIndicator extension if you want a tray icon.

## First start

The setup wizard has five short steps. It finds or installs ClamAV, downloads the virus definitions (about 120 MB the first time) and asks which protections you want. You can change every choice later in Settings.

## Where Clamshield keeps its files

| System | Folder |
|---|---|
| Linux | `~/.config/Clamshield` |
| macOS | `~/.config/Clamshield` |
| Windows | `%APPDATA%\Clamshield`, and `%LOCALAPPDATA%\Clamshield` for the large files |

That folder holds your settings, history, quarantined files, the virus database and the logs. On Windows the virus database and the ClamAV that the wizard downloads go in `%LOCALAPPDATA%`, so they are not copied around with a roaming profile. On Linux and macOS the folder is readable only by your account. [docs/PRIVACY.md](PRIVACY.md) lists what is stored and what is sent over the network.

## Updating

Download the new release and replace the old files. On Linux, run `./install.sh` again. Your settings stay where they are. Virus definitions update on their own and do not depend on the app version.

Clamshield does not update itself yet.

## Removing

- On Windows, delete the folder you unzipped. To remove the settings as well, delete `%APPDATA%\Clamshield` and `%LOCALAPPDATA%\Clamshield`.
- On macOS, drag Clamshield to the Trash. To remove the settings as well, delete `~/.config/Clamshield`.
- On Linux, run `./uninstall.sh` from the extracted folder. To remove the settings as well, run `rm -rf ~/.config/Clamshield`.

If you turned on "start with the computer" or the right-click menu, turn them off in Settings first, so those entries are cleaned up too.
