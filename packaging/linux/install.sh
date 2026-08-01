#!/bin/sh
# TrayAuth for Linux - per-user install. No sudo: everything goes under ~/.local.
#
# POSIX sh on purpose, and meant to be started as `sh install.sh` or `bash install.sh`:
# this tarball is built on Windows, where tar cannot record the executable bit, so the
# script must work without one. It restores proper modes on everything it installs.
set -eu

# Per-user install: running under sudo would silently install into /root's home instead of
# yours. Refuse outright - "sudo to install" is muscle memory worth interrupting here.
if [ "$(id -u)" -eq 0 ]; then
    echo "Do NOT run this with sudo." >&2
    echo "TrayAuth installs into YOUR home directory. Run it as yourself:" >&2
    echo "    bash install.sh" >&2
    exit 1
fi

HERE="$(cd "$(dirname "$0")" && pwd)"
APP_DIR="$HOME/.local/share/trayauth"
BIN_DIR="$HOME/.local/bin"
DESKTOP_DIR="$HOME/.local/share/applications"
ICON_DIR="$HOME/.local/share/icons/hicolor/scalable/apps"

echo
echo "Installing TrayAuth"
echo "-------------------"

mkdir -p "$APP_DIR" "$BIN_DIR" "$DESKTOP_DIR" "$ICON_DIR"

install -m 755 "$HERE/trayauth" "$APP_DIR/trayauth"
install -m 644 "$HERE/trayauth.svg" "$ICON_DIR/trayauth.svg"
ln -sf "$APP_DIR/trayauth" "$BIN_DIR/trayauth"

# Point the desktop entry at the installed binary.
sed "s|__EXEC__|$APP_DIR/trayauth|" "$HERE/trayauth.desktop" > "$DESKTOP_DIR/trayauth.desktop"
chmod 644 "$DESKTOP_DIR/trayauth.desktop"

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DESKTOP_DIR" || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q "$HOME/.local/share/icons/hicolor" || true

echo
echo "Installed."
echo
echo "  Binary     $APP_DIR/trayauth"
echo "  Accounts   ~/.config/trayauth/ (created on first run, encrypted)"
echo
echo "First, prove the core works on this machine:"
echo
echo "    \"$BIN_DIR/trayauth\" --selftest"
echo
echo "Then start it:"
echo
echo "    \"$BIN_DIR/trayauth\" &"
echo
echo "Recommended packages:"
echo "    sudo apt install wl-clipboard      # copying from the tray menu on Wayland"
echo "    sudo apt install libsecret-tools   # store the vault key in the GNOME keyring"
echo
