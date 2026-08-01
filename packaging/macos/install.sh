#!/bin/sh
# TrayAuth for macOS - drops TrayAuth.app into ~/Applications and clears the download
# quarantine flag so Gatekeeper does not block the first launch.
#
# POSIX sh, runnable as `sh install.sh`: the archive is produced on Windows, which cannot
# record executable bits, so nothing here may depend on one.
set -eu

if [ "$(id -u)" -eq 0 ]; then
    echo "Do NOT run this with sudo - TrayAuth installs into your own home directory." >&2
    exit 1
fi

HERE="$(cd "$(dirname "$0")" && pwd)"
APP_NAME="TrayAuth.app"
DEST_DIR="$HOME/Applications"
DEST="$DEST_DIR/$APP_NAME"

echo
echo "Installing TrayAuth"
echo "-------------------"

if [ ! -d "$HERE/$APP_NAME" ]; then
    echo "TrayAuth.app not found next to this script." >&2
    exit 1
fi

# Stop a running copy so the bundle can be replaced cleanly.
pkill -x trayauth 2>/dev/null || true
sleep 1

mkdir -p "$DEST_DIR"
rm -rf "$DEST"
cp -R "$HERE/$APP_NAME" "$DEST"

# The tar was built on Windows: restore the executable bit the binary needs.
chmod +x "$DEST/Contents/MacOS/trayauth"

# Gatekeeper refuses downloaded apps that are neither signed nor notarized. Clearing the
# quarantine attribute on a bundle the user deliberately installed is the standard escape
# hatch, and is exactly what right-click -> Open does behind the scenes.
xattr -dr com.apple.quarantine "$DEST" 2>/dev/null || true

echo
echo "Installed to $DEST"
echo
echo "First, prove the core works on this machine:"
echo
echo "    \"$DEST/Contents/MacOS/trayauth\" --selftest"
echo
echo "Then start it:"
echo
echo "    open \"$DEST\""
echo
echo "TrayAuth lives in the menu bar - look for the keyhole icon near the clock."
echo "It has no Dock icon by design."
echo
