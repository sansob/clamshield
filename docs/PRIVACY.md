# Privacy

Clamshield has no account, no analytics, no crash reporting and no advertising. It does not phone home. Everything it knows about you stays on your computer, unless you use one of the features below.

## What can leave your computer

| When | Where it goes | What is sent |
|---|---|---|
| Virus definitions update | ClamAV's mirrors, `database.clamav.net` | A normal update request. ClamAV's updater (`freshclam`) identifies itself with its version and keeps a small identifier file, `freshclam.dat`, in the database folder. That is ClamAV's own behaviour, and Clamshield adds nothing to it |
| "Extra virus signatures" is turned on (it is off by default) | The Sanesecurity files on `ftp.swin.edu.au`, a university mirror | A normal download request for five small files |
| Check a file online | `virustotal.com` | The SHA-256 fingerprint of the file, and your own VirusTotal API key. Never the file |
| Check a link | `virustotal.com` | The link. If VirusTotal has not seen it before, it is submitted for analysis, and VirusTotal may then show it to others |
| Password check | `api.pwnedpasswords.com` | The first five characters of the SHA-1 hash of the password. The rest of the hash and the password itself stay on your computer, and the service returns every hash that starts with those five characters. The input box is cleared right after the check |
| The setup wizard installs ClamAV on Windows or macOS | `api.github.com` and `github.com` | A request for the latest ClamAV release, and its download |
| The setup wizard installs ClamAV on Linux | Your distribution's package servers, through your package manager | What the package manager normally sends |

Nothing is sent in the background except the virus definition update, and that one you can turn off by not using the Updates page and by switching off the update before scheduled scans.

Opening a web link from a button in Clamshield, for example "Open on VirusTotal", opens your own browser. That is a normal visit from the browser, not from Clamshield.

## What is stored on your computer

All of it is in the data folder described in [INSTALL.md](INSTALL.md), readable only by your account on Linux and macOS.

- Settings, including the VirusTotal key if you entered one. The key is stored as plain text in `settings.json`. The file is private to your account, but any program running as you can read it. If that worries you, use a key you can revoke, which a free VirusTotal key is.
- Scan history: the date, type, file count and result of the latest 200 scans and detections.
- The real-time log: the paths and names of files that were checked, and what was found. It is a plain text file, rotated at 1 MB. Paths can reveal what you work on. The Clear button in the dashboard only empties the list on screen. To remove the history of checked files, delete `logs/realtime.log` yourself.
- Quarantine: the infected files themselves, with their original path and the name of the threat.
- Block list, trusted files, startup baseline: SHA-256 hashes and paths.
- Reports: a weekly summary written to the `reports` folder.

Nothing in the data folder is uploaded anywhere by Clamshield.

## Removing your data

Uninstalling the app leaves the data folder in place. Delete it by hand to remove everything. [INSTALL.md](INSTALL.md) has the exact paths.

## Questions

If something here is unclear or you find a behaviour that does not match this page, please open an issue. If you think it is a security problem, follow [SECURITY.md](../SECURITY.md) instead.
