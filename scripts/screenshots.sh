#!/usr/bin/env bash
# Regenerates the screenshots in docs/screenshots.
#
# The app renders its own screens to PNG when CLAMSHIELD_SHOTS is set (see src/Clamshield/MainWindow.Screenshots.cs),
# so no screen capture tool is needed. It runs against made-up demo data in a throwaway folder and never touches
# your real settings. Needs: bash, python3, the .NET SDK, and ClamAV (for the scan result screenshot).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/docs/screenshots"
WORK="$(mktemp -d)"
DEMO=/tmp/clamshield-demo            # shown on screen in the scan result, so keep it short
trap 'rm -rf "$WORK" "$DEMO"' EXIT

echo "Building..."
dotnet build "$ROOT/src/Clamshield/Clamshield.csproj" --configuration Debug --nologo -v q
APP="$ROOT/src/Clamshield/bin/Debug/net10.0/Clamshield.dll"

# --- demo files for the scan result screenshot (the EICAR text is a harmless antivirus test string)
rm -rf "$DEMO"; mkdir -p "$DEMO/Downloads"
for i in $(seq 1 40); do head -c 30000 /dev/urandom > "$DEMO/Downloads/photo$i.dat"; done
printf '%s' 'X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*' > "$DEMO/Downloads/invoice_2026.exe"

# --- demo settings, history, quarantine and block list
mkdir -p "$WORK/config/Clamshield/quarantine"
python3 - "$WORK/config/Clamshield" <<'PY'
import json, sys, datetime as d
base = sys.argv[1]; now = d.datetime.now()
iso = lambda minutes: (now - d.timedelta(minutes=minutes)).isoformat()
json.dump([
  {"Date": iso(5),      "Type": "Real-time",  "Files": 1,      "Threats": 1, "Seconds": 0,    "Status": "Blocked"},
  {"Date": iso(90),     "Type": "Quick Scan", "Files": 18423,  "Threats": 0, "Seconds": 83,   "Status": "Clean"},
  {"Date": iso(60*26),  "Type": "Scheduled",  "Files": 20112,  "Threats": 2, "Seconds": 97,   "Status": "Threats found"},
  {"Date": iso(60*50),  "Type": "USB Scan",   "Files": 640,    "Threats": 0, "Seconds": 12,   "Status": "Clean"},
  {"Date": iso(60*74),  "Type": "Full Scan",  "Files": 312004, "Threats": 0, "Seconds": 1450, "Status": "Cancelled"},
  {"Date": iso(60*98),  "Type": "Quick Scan", "Files": 17001,  "Threats": 0, "Seconds": 79,   "Status": "Clean"},
], open(base + "/history.json", "w"))
json.dump([
  {"Id": "a1", "OriginalPath": "/home/user/Downloads/invoice_2026.exe", "Virus": "Win.Trojan.Agent-1234",  "Date": iso(5),     "FileName": "invoice_2026.exe", "DateText": "x"},
  {"Id": "a2", "OriginalPath": "/home/user/Documents/crack/keygen.bin", "Virus": "Win.Malware.Generic-98", "Date": iso(60*30), "FileName": "keygen.bin",       "DateText": "x"},
], open(base + "/quarantine/index.json", "w"))
json.dump({"Items": [
  {"Sha256": "a" * 64, "Size": 5000, "Name": "Clamshield.Blocked.cracked_installer.exe", "Path": "/home/user/Downloads/cracked_installer.exe", "Added": now.isoformat()},
]}, open(base + "/blocklist.json", "w"))
json.dump({"SetupDone": True, "ScheduleEnabled": True, "WatchFolders": ["/home/user/Pictures"], "Exclusions": ["/home/user/Games"],
           "Profiles": [{"Name": "Project files", "Paths": ["/home/user/Documents/Code"]}], "LastUpdate": iso(60*3)},
          open(base + "/settings.json", "w"))
PY

for theme in dark light; do
  echo "Rendering the $theme theme..."
  mkdir -p "$WORK/shots-$theme"
  XDG_CONFIG_HOME="$WORK/config" CLAMSHIELD_SIZE=1280x800 CLAMSHIELD_THEMES="$theme" \
  CLAMSHIELD_SCANDIR="$DEMO/Downloads" CLAMSHIELD_SHOTS="$WORK/shots-$theme" \
    timeout 400 dotnet "$APP" > "$WORK/run-$theme.log" 2>&1 || true
  grep -q "SHOTS DONE" "$WORK/run-$theme.log" || { echo "The $theme run did not finish, see below"; tail -20 "$WORK/run-$theme.log"; exit 1; }
  pkill -x clamd 2>/dev/null || true
done

mkdir -p "$OUT"
pick() { cp "$WORK/shots-$1/$1-$2.png" "$OUT/$3.png"; }
pick dark  20-home-log        home-dark
pick light 20-home-log        home-light
pick dark  19-home-outdated   home-update-needed
pick dark  23-scan-done       scan-result
pick dark  03-tools           tools-dark
pick light 03-tools           tools-light
pick dark  09c-tool-engine    engine-details
pick dark  11-settings1       settings-protection
pick light 11-settings2       settings-scanning
pick dark  11-settings3       quarantine
pick light 11-settings4       history
pick dark  10-updates         updates
pick dark  12-wizard0         setup-wizard
pick light 11-settings5       about

# smaller files, same pixels
python3 - "$OUT" <<'PY' || true
import sys, glob
try:
    from PIL import Image
except ImportError:
    sys.exit(0)
for f in glob.glob(sys.argv[1] + "/*.png"):
    Image.open(f).save(f, optimize=True)
PY
echo "Done. Screenshots are in $OUT"
