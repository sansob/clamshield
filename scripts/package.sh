#!/usr/bin/env bash
# Turns a published build into the file people download.
#
#   scripts/package.sh <rid> <version> <publish-dir> <output-dir>
#
# linux-*  -> Clamshield-<version>-<rid>.tar.gz   (binary + install.sh / uninstall.sh)
# osx-*    -> Clamshield-<version>-<rid>.dmg      (a .app bundle, ad-hoc signed when codesign exists)
# Windows is zipped by the release workflow itself (PowerShell), see .github/workflows/release.yml.
set -euo pipefail

if [ "$#" -ne 4 ]; then
  echo "usage: $0 <rid> <version> <publish-dir> <output-dir>" >&2
  exit 2
fi
RID="$1"; VERSION="$2"; SRC="$3"; OUT="$4"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSETS="$ROOT/src/Clamshield/Assets"
NAME="Clamshield-$VERSION-$RID"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

case "$RID" in
  linux-*)
    STAGE="$WORK/$NAME"
    mkdir -p "$STAGE"
    cp -a "$SRC"/. "$STAGE/"
    rm -f "$STAGE"/*.pdb
    chmod +x "$STAGE/Clamshield"
    cp "$ROOT/scripts/linux/install.sh" "$ROOT/scripts/linux/uninstall.sh" "$STAGE/"
    chmod +x "$STAGE/install.sh" "$STAGE/uninstall.sh"
    cp "$ROOT/LICENSE" "$ROOT/README.md" "$ASSETS/icon.png" "$ASSETS/icon.svg" "$STAGE/"
    tar -C "$WORK" -czf "$OUT/$NAME.tar.gz" "$NAME"
    echo "created $OUT/$NAME.tar.gz"
    ;;

  osx-*)
    APP="$WORK/Clamshield.app"
    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
    cp -a "$SRC"/. "$APP/Contents/MacOS/"
    rm -f "$APP/Contents/MacOS/"*.pdb
    chmod +x "$APP/Contents/MacOS/Clamshield"
    cp "$ASSETS/icon.icns" "$APP/Contents/Resources/Clamshield.icns"
    cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Clamshield</string>
  <key>CFBundleDisplayName</key><string>Clamshield</string>
  <key>CFBundleIdentifier</key><string>io.github.sansob.clamshield</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>Clamshield</string>
  <key>CFBundleIconFile</key><string>Clamshield</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
    # Apple silicon refuses to run unsigned binaries, so sign ad hoc (no certificate needed).
    if command -v codesign >/dev/null 2>&1; then
      codesign --force --deep --sign - "$APP"
    fi
    MADE_DMG=0
    if command -v hdiutil >/dev/null 2>&1; then
      DMG="$WORK/dmg"
      mkdir -p "$DMG"
      mv "$APP" "$DMG/"
      ln -s /Applications "$DMG/Applications"
      # hdiutil sizes the image itself and can come up short ("No space left on device"), so give it room.
      MB=$(( $(du -sm "$DMG" | cut -f1) * 13 / 10 + 40 ))
      if hdiutil create -size "${MB}m" -fs HFS+ -volname "Clamshield" -srcfolder "$DMG" -ov -format UDZO "$OUT/$NAME.dmg"; then
        echo "created $OUT/$NAME.dmg"; MADE_DMG=1
      else
        echo "hdiutil failed, falling back to a zip" >&2
        mv "$DMG/Clamshield.app" "$WORK/"
      fi
    fi
    if [ "$MADE_DMG" -eq 0 ]; then
      (cd "$WORK" && zip -qry "$OUT/$NAME.app.zip" Clamshield.app)
      echo "created $OUT/$NAME.app.zip"
    fi
    ;;

  *)
    echo "package.sh does not handle '$RID'" >&2
    exit 2
    ;;
esac
