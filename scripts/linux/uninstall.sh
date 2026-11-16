#!/usr/bin/env bash
# Removes what install.sh put in place. Your settings in ~/.config/Clamshield are left alone.
set -euo pipefail
PREFIX="${CLAMSHIELD_PREFIX:-$HOME/.local/opt/clamshield}"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"

rm -rf "$PREFIX"
rm -f "$HOME/.local/bin/clamshield" \
      "$DATA/applications/clamshield.desktop" \
      "$DATA/icons/hicolor/scalable/apps/clamshield.svg" \
      "$DATA/icons/hicolor/512x512/apps/clamshield.png"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DATA/applications" || true
echo "Clamshield was removed. To delete its settings too: rm -rf ~/.config/Clamshield"
