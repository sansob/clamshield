# Third-party notices

Clamshield is released under the [MIT license](LICENSE). It builds on the software and services below. Each keeps its own license and terms.

## Included in the released apps

| Component | Used for | License |
|---|---|---|
| [.NET runtime](https://dotnet.microsoft.com/) | Runs the app. It is bundled, so users install nothing | MIT |
| [Avalonia](https://avaloniaui.net/) and its Fluent theme | The user interface toolkit | MIT |
| [Inter](https://rsms.me/inter/), through the Avalonia.Fonts.Inter package | Fallback font | SIL Open Font License 1.1 |
| [Plus Jakarta Sans](https://github.com/tokotype/PlusJakartaSans) | The app font. The license text is next to the font files in `src/Clamshield/Assets/Fonts` | SIL Open Font License 1.1 |
| [Material Design icons](https://github.com/google/material-design-icons) | Several of the interface icons are drawn from these paths | Apache License 2.0 |

The Avalonia developer tools are referenced in debug builds only and are not part of the releases.

## Used at run time, not included

| Component | How it is used | License |
|---|---|---|
| [ClamAV](https://www.clamav.net/) | The scanning engine. Clamshield starts its programs as separate processes. They are neither linked into Clamshield nor shipped with it. The setup wizard installs them from the official source on the user's machine | GPL-2.0 |

ClamAV is a trademark of Cisco Systems, Inc. Clamshield is an independent project and is not made, endorsed or supported by the ClamAV team or by Cisco.

## Optional online services

These are only contacted when the user uses the matching feature, as described in [docs/PRIVACY.md](docs/PRIVACY.md).

| Service | Feature |
|---|---|
| [VirusTotal](https://www.virustotal.com/) | Checking a file or a link. Users bring their own API key. VirusTotal's public API is meant for non-commercial use, see their terms |
| [Have I Been Pwned: Pwned Passwords](https://haveibeenpwned.com/API/v3#PwnedPasswords), by Troy Hunt | The password check |
| [Sanesecurity](https://sanesecurity.com/) signature files, from the mirror at ftp.swin.edu.au | The optional extra virus signatures. They have their own terms |

## Test data

The EICAR test string used in tests and in the documentation is the standard antivirus test file from the [EICAR](https://www.eicar.org/) organisation. It is harmless.
