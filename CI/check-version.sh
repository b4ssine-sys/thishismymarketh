#!/usr/bin/env bash
# WO-33: the tag, Mod.Version and the csproj <Version> must agree before a
# release is built. Usage: CI/check-version.sh [tag]   (tag like v1.0.0)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
mod=$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' "$root/MyFirstMod/Mod.cs")
proj=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$root/MyFirstMod/MyFirstMod.csproj")
echo "Mod.Version=$mod csproj=$proj tag=${1:-<none>}"
[ -n "$mod" ] && [ -n "$proj" ] || { echo "::error::could not read a version"; exit 1; }
[ "$mod" = "$proj" ] || { echo "::error::Mod.Version ($mod) != csproj Version ($proj)"; exit 1; }
if [ -n "${1:-}" ] && [[ "$1" == v* ]]; then
  [ "${1#v}" = "$mod" ] || { echo "::error::tag $1 does not match version $mod"; exit 1; }
fi
