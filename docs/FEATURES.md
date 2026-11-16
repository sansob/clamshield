# Features

This is the long version of what Clamshield does and how each part behaves. Where something has a limit, it says so.

## Scanning

### Scan types

| Scan | What it checks |
|---|---|
| Smart scan and quick scan | Downloads, Desktop, Documents and the temporary folder |
| Full scan | Every file on the device. On Windows that means every fixed drive, elsewhere the whole file system without `/proc`, `/sys`, `/dev` and a few other virtual folders |
| Custom scan | One folder you pick |
| Drag and drop | Anything you drop on the window, files or folders |
| Running programs | The program file behind every process the current user can see. Programs started from a temporary or download folder, or whose file was deleted while they run, are called out |
| Scan profiles | A named list of folders, saved in Settings, started with one click |

Before scanning, Clamshield walks the folders and counts the files, which is what makes the progress ring show a real percentage. Symbolic links are skipped so a scan cannot wander out of the folder you chose.

### How the scan runs

When the private ClamAV daemon is ready, scans go through it with `clamdscan --multiscan`. The virus database is already in memory, so a scan starts at once. Before the daemon is ready, or if it fails, Clamshield falls back to plain `clamscan` automatically. See [ARCHITECTURE.md](ARCHITECTURE.md) for the details.

Scheduled scans run at a lower process priority.

### Scan options (Settings, Scanning)

- Look inside archives (on by default).
- Largest file to scan: 25, 50, 100, 250 or 500 MB. Bigger files are skipped.
- Skip photos, music and videos for faster scans.
- Strict detection: also flags adware and other unwanted programs, Office files with macros, and encrypted archives and documents. Expect a few more false alarms.
- Extra virus signatures: five small community lists from Sanesecurity (Office macros, fake antivirus, known malware hashes). They are downloaded together with your normal updates. Off by default, and they add a few thousand signatures to the millions in the official database.
- Exclusions: files and folders that scans and real-time protection skip.

## Real-time protection

Clamshield watches Downloads and Desktop, any folders you add under Settings, Protection, and optionally the mail folders of Thunderbird and Evolution.

When a file appears or changes:

1. It is queued. The queue holds at most 2000 files.
2. Clamshield waits until the file stops growing and can be opened, so half-downloaded files are not scanned.
3. Files are grouped into batches of up to 64 and scanned together at below-normal priority. With the daemon ready a batch waits 0.4 seconds to collect neighbours. Without it, 3 seconds, because every `clamscan` call has to load the database.
4. A short pause between batches keeps a flood of files from pinning the processor.

Files with temporary extensions (`.crdownload`, `.part`, `.tmp` and similar), excluded files, skipped media types and files above the size limit are ignored. A file that was just scanned and has not changed is not scanned again within five seconds.

A threat is moved to quarantine right away unless you turned that off. The dashboard shows what is being checked, how many files are queued, checked and blocked, and a log of the latest 100 events. The full log is written to `logs/realtime.log` in the data folder and rotated at 1 MB. Settings, General has a button that opens it.

### Pausing

Settings, Protection can pause real-time protection for 15 minutes, an hour, or until Clamshield restarts. While paused, USB scanning, scheduled scans and the ransomware shield also stop, and the dashboard says so with a Resume button. The tray menu can pause for 15 minutes and resume.

### Ransomware shield

Clamshield watches Documents, Desktop and Pictures. It raises a warning when 120 different files change within ten seconds, or when 15 or more files are renamed to the same unusual extension. Build folders and caches of developer tools (`bin`, `obj`, `node_modules`, `.git` and similar) are ignored. After a warning there is a one-minute quiet period.

### Startup watch

Every five minutes Clamshield compares a snapshot of the places malware uses to survive a reboot with the previous one and warns about additions and changes.

- Linux: autostart entries, user systemd units, your crontab, `authorized_keys`, shell profiles and `/etc/ld.so.preload`.
- macOS: launch agents and daemons, your crontab, shell profiles and `authorized_keys`.
- Windows: the Run and RunOnce registry keys, the Startup folders and scheduled task names.

The first run only records a baseline, so you are not warned about everything that already exists. Clamshield's own start-up entry is ignored.

### USB drives

When a removable drive appears, it is scanned in the background and you get a notification with the result. Drives that were already connected when Clamshield started are not scanned.

## Quarantine

Infected files are moved to `quarantine` in the data folder under a random name. On Linux and macOS the quarantined copy loses its execute permission and the original permissions are stored, so they come back when you restore the file.

For each item you can restore it, restore it and trust it (see below), check it on VirusTotal, or delete it. Quarantine can empty itself after 7, 30 or 90 days. The default is 30.

## Handling false alarms and adding your own rules

### Trusted files

"Restore and trust" remembers the SHA-256 of the file. If the file is flagged again, even in another place, it is ignored. If the file changes, the trust no longer applies.

### Block list

Tools, Block a file takes the SHA-256 and size of any file and writes them into a small ClamAV hash database. From then on the file and every renamed copy of it is reported as `Clamshield.Blocked.<name>`. This works once Clamshield uses its own database, which happens after the first definitions update.

### YARA rules

Settings, Scanning, YARA rules installs `.yar` files into the same database folder, where ClamAV loads them. ClamAV supports only part of the YARA language, and it quietly ignores a rule it cannot use. So Clamshield first loads each rule with the real engine and shows you the error if it fails.

## Tools

| Tool | What it does | What leaves your computer |
|---|---|---|
| Check a file online | Looks up the file's SHA-256 on VirusTotal | The hash, with your own API key |
| Check a link | Looks the URL up on VirusTotal, and submits it for analysis if VirusTotal has not seen it | The URL |
| Password check | Asks Have I Been Pwned how often the password appears in breaches | The first five characters of its SHA-1 hash |
| File shredder | Overwrites a file three times, renames it and deletes it | Nothing |
| Startup programs | Lists what starts at login and lets you switch entries off and on again | Nothing |
| Disk clean-up | Removes old temporary files, thumbnail caches and the trash | Nothing |
| Block a file | See above | Nothing |
| Engine details | Shows the ClamAV version, the loaded databases with their signature counts, and the daemon status | Nothing |

Some details worth knowing:

- The VirusTotal key is free from virustotal.com. The free tier allows 4 lookups per minute.
- The shredder refuses to touch the root folder, your home folder and system folders, and never follows a link to a file outside the one you picked. On SSDs and on copy-on-write file systems an overwrite cannot guarantee the old data is gone.
- Disabling a startup entry is reversible. On Linux, switching off a system-wide entry writes an override into your own autostart folder and leaves the system file alone. Entries that need administrator rights to change are shown but locked.
- Disk clean-up only removes files older than two days from the temporary folder, never follows links and never leaves the folders it is allowed to clean.

## Dashboard

- A banner with your protection status. It changes colour and wording when the engine is missing, threats are waiting, protection is paused, the definitions are out of date, or you have never scanned.
- A notice with an Update now button when the virus definitions are more than three days old, or have never been downloaded.
- The real-time panel described above.
- A security health score out of 100: engine installed (25), definitions up to date (25), real-time protection on (20), a scan in the last seven days (15), scheduled scans on (10) and the ransomware shield on (5). Each missing item has a button that fixes it.
- A seven-day chart of files checked and threats found.

## Updates

The Updates page downloads new virus definitions with `freshclam`, using a private configuration and a database folder inside the data folder. That is why no administrator rights are needed: the system-wide `freshclam` usually can only be run by root. It shows a progress ring while it works, and the technical log is under Settings, General.

Definitions can also update before each scheduled scan.

## Everyday conveniences

- First-run setup wizard. It also reappears if ClamAV goes missing later.
- Notifications through the system: `notify-send` on Linux, `osascript` on macOS, a PowerShell toast on Windows. A small in-app notice is shown as well while the window is open.
- Tray icon with Open, Quick Scan, Pause, Resume and Quit. It is off by default on Linux because GNOME has no tray.
- Start with the computer: a launcher entry on Linux, a launch agent on macOS, a registry Run entry on Windows. It starts hidden in the tray when the tray is on.
- Right-click "Scan with Clamshield" in the file manager: Nautilus, Nemo and Dolphin on Linux, Explorer on Windows. Not available on macOS.
- Only one copy runs at a time. Starting a second one hands its request, for example a folder to scan, to the first.
- Scan history with HTML and plain-text reports, and a weekly summary notification. Reports are saved in the data folder under `reports`.
- Light, dark and follow-the-system themes.
- English and Bahasa Indonesia. See [TRANSLATING.md](TRANSLATING.md) to help with another language.

## Command line

```
clamshield --cli --quick               scan Downloads, Desktop, Documents (also the default)
clamshield --cli --full                scan the whole device
clamshield --cli --scan PATH...        scan specific files or folders
clamshield --cli --running             scan the programs that are running now
clamshield --cli --block FILE          add a file to the block list
clamshield --cli --yara-add FILE       install a YARA rule file
clamshield --cli --engine              show the loaded databases
clamshield --cli --help
```

Add `--json` to a scan for machine-readable output. The exit code is 0 for a clean result, 1 when something was found and 2 on an error. The command line reads the same settings as the app.

## What it does not do

- It does not watch program behaviour. A threat ClamAV has no signature for is not caught.
- It does not block network traffic or act as a firewall.
- It does not scan inside encrypted files, apart from reporting that they are encrypted in strict mode.
- It cannot scan other users' processes, or files your account is not allowed to read.
