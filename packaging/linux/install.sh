#!/usr/bin/env bash
# TrayAuth for Linux - per-user install. No sudo: everything goes under ~/.local.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
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
echo "  Binary     $APP_DIR/trayauth   (also on PATH as 'trayauth' if ~/.local/bin is in PATH)"
echo "  Accounts   ~/.config/trayauth/ (created on first run, encrypted)"
echo
echo "First, prove the core works on this machine:"
echo
echo "    trayauth --selftest"
echo
echo "Then start it:"
echo
echo "    trayauth &"
echo
echo "Recommended packages:"
echo "    sudo apt install wl-clipboard      # copying from the tray menu on Wayland"
echo "    sudo apt install libsecret-tools   # store the vault key in the GNOME keyring"
echo
