#!/usr/bin/env bash
# Installs Clamshield for the current user only. No administrator rights needed.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${CLAMSHIELD_PREFIX:-$HOME/.local/opt/clamshield}"
BIN="$HOME/.local/bin"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"

mkdir -p "$PREFIX" "$BIN" "$DATA/applications" "$DATA/icons/hicolor/scalable/apps" "$DATA/icons/hicolor/512x512/apps"
# The native AOT build keeps SkiaSharp and HarfBuzz next to the program, so copy those too.
for f in "$HERE"/Clamshield "$HERE"/*.so; do [ -e "$f" ] && cp -a "$f" "$PREFIX/"; done
chmod +x "$PREFIX/Clamshield"
ln -sf "$PREFIX/Clamshield" "$BIN/clamshield"

cp "$HERE/icon.svg" "$DATA/icons/hicolor/scalable/apps/clamshield.svg"
cp "$HERE/icon.png" "$DATA/icons/hicolor/512x512/apps/clamshield.png"

cat > "$DATA/applications/clamshield.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Version=1.0
Name=Clamshield
GenericName=Antivirus
Comment=A friendly antivirus built on ClamAV
Exec="$PREFIX/Clamshield"
Icon=clamshield
Terminal=false
Categories=System;Security;
Keywords=antivirus;clamav;virus;malware;scan;security;
StartupNotify=true
StartupWMClass=clamshield
DESKTOP

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DATA/applications" || true
command -v gtk-update-icon-cache  >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f "$DATA/icons/hicolor" || true

echo "Clamshield is installed in $PREFIX"
echo "Start it from your app menu, or run: clamshield"
case ":$PATH:" in *":$BIN:"*) ;; *) echo "Note: $BIN is not on your PATH yet." ;; esac
