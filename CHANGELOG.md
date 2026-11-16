# Changelog

All notable changes to Clamshield are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

## [1.0.0] - 2026-11-16

First public release.

### Protection
- Real-time protection for Downloads and Desktop, plus any folders you add. New and changed files are scanned in small low-priority batches.
- A private ClamAV daemon keeps the virus database in memory, so scans start at once instead of after a few seconds of loading.
- Quick, full, custom and drag-and-drop scans, scans of the programs that are running right now, and scan profiles for folders you check often.
- USB drives are scanned when you plug them in.
- Ransomware shield: a warning when many documents change or are renamed at once.
- Startup watch: a warning when a startup entry, scheduled job, SSH key or shell profile changes.
- Scheduled scans that wait while you are on battery or running a full-screen app.
- Pause real-time protection for 15 minutes, an hour, or until restart.

### Detection
- Strict mode for adware, risky Office macros and password-protected archives.
- Optional community signature lists from Sanesecurity.
- Your own block list: block a file by its fingerprint and every copy is flagged.
- YARA rules, checked by the engine before they are installed.
- Trusted files, so a false alarm is dismissed once and stays dismissed.

### Tools
- Check a file or a link on VirusTotal. Only fingerprints are sent.
- Check a password against known breaches with k-anonymity. The password never leaves your computer.
- File shredder, startup program manager, disk clean-up and an engine details page.

### Everyday use
- First-run wizard that installs ClamAV when it is missing and downloads the virus definitions.
- A dashboard with a security health score, a seven-day summary, a live activity log and a notice when the definitions are out of date.
- Quarantine with restore, delete, automatic clean-up and a restore-and-trust option.
- Scan history with HTML and text reports, and a weekly summary.
- Light and dark themes, English and Bahasa Indonesia, tray icon, start with the computer, right-click "Scan with Clamshield" on Windows and Linux, and a command line mode.

### Safety of the app itself
- Settings, history and quarantine live in a folder only your account can read.
- Quarantined files lose their execute permission and get it back when restored.

[Unreleased]: https://github.com/sansob/clamshield/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/sansob/clamshield/releases/tag/v1.0.0
