#!/usr/bin/env bash
# WO-33: download the game's managed assemblies for the release build.
# Usage: CI/fetch-game-assemblies.sh <dest-dir>
# Env:   ZIP_URL (required), ZIP_TOKEN (optional bearer), ZIP_SHA256 (optional pin)
# Exit 2 when ZIP_URL is unset, so callers can tell "not configured" from "broken".
set -euo pipefail
dest="${1:?destination directory}"

if [ -z "${ZIP_URL:-}" ]; then
  echo "::warning::CS_MANAGED_ZIP_URL is not set; cannot build the shipped DLL. See .github/workflows/release.yml."
  exit 2
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
if [ -n "${ZIP_TOKEN:-}" ]; then
  curl -fsSL -H "Authorization: Bearer ${ZIP_TOKEN}" -H "Accept: application/octet-stream" -o "$tmp/managed.zip" "$ZIP_URL"
else
  curl -fsSL -o "$tmp/managed.zip" "$ZIP_URL"
fi

if [ -n "${ZIP_SHA256:-}" ]; then
  echo "${ZIP_SHA256}  $tmp/managed.zip" | sha256sum -c -
fi

mkdir -p "$dest"
unzip -j -o -q "$tmp/managed.zip" '*.dll' -d "$dest"
for name in Assembly-CSharp ColossalManaged ICities UnityEngine; do
  if [ ! -f "$dest/$name.dll" ]; then
    echo "::error::$name.dll missing from the game-assembly zip"
    exit 1
  fi
done
echo "Game assemblies ready in $dest"
