# Security policy

Clamshield handles untrusted files and starts programs on your behalf, so security reports matter here.

## Reporting a vulnerability

Please do not open a public issue for a vulnerability. Use GitHub's private reporting instead: go to the **Security** tab of the repository and choose **Report a vulnerability**.

Include what you found, the steps to reproduce it, the version and the operating system. A short proof of concept helps.

This is a volunteer project, so there is no guaranteed response time. We will read your report, tell you what we think, and credit you in the release notes if you like once a fix is out.

## What counts

Things we want to hear about:

- A way to make Clamshield run a command or overwrite a file that the user did not choose, for example through a crafted file name.
- A way to read or change Clamshield's settings, quarantine or block list from another user account.
- A way to make the file shredder, the clean-up tool or quarantine restore touch files outside what they should.
- A quarantined file that can be executed, or a restore that puts a file somewhere it should not go.
- A leak of the VirusTotal key or of data that [docs/PRIVACY.md](docs/PRIVACY.md) says stays local.
- A flaw in the release pipeline that could let someone ship a tampered build.

## What does not

- A virus that ClamAV does not detect. That is a detection question, and the ClamAV project is the place for it.
- A vulnerability in ClamAV itself. Please report it to [ClamAV](https://www.clamav.net/) directly.
- The fact that the builds are not code-signed. That is known, and [docs/RELEASING.md](docs/RELEASING.md) explains it.
- Problems that need an attacker who already has full control of your account.

## Supported versions

Only the latest release gets security fixes.
