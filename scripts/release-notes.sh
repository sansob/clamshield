#!/usr/bin/env bash
# Prints the CHANGELOG.md section for one version, for use as GitHub release notes.
#   scripts/release-notes.sh 1.2.0
set -euo pipefail
VERSION="${1:?version required}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
awk -v v="$VERSION" '
  $0 ~ "^## \\[" v "\\]" { found = 1; next }
  found && /^## \[/      { exit }
  found                  { print }
' "$ROOT/CHANGELOG.md"
