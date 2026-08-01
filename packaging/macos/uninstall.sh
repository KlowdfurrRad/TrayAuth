#!/bin/sh
# Removes TrayAuth from macOS. Accounts are kept unless you say otherwise.
set -eu

APP="$HOME/Applications/TrayAuth.app"
AGENT="$HOME/Library/LaunchAgents/io.github.klowdfurrrad.trayauth.plist"
CONFIG="$HOME/Library/Application Support/TrayAuth"

echo
echo "Uninstalling TrayAuth"
echo "---------------------"

pkill -x trayauth 2>/dev/null || true
sleep 1

if [ -f "$AGENT" ]; then
    launchctl unload "$AGENT" 2>/dev/null || true
    rm -f "$AGENT"
    echo "Removed the start-on-login agent."
fi

rm -rf "$APP"
echo "Removed $APP"
echo

if [ -d "$CONFIG" ]; then
    echo "Your accounts are still at:"
    echo "  $CONFIG"
    printf "Delete them too? They cannot be recovered afterwards. [y/N] "
    read -r answer || answer=""
    case "$answer" in
        [Yy]*)
            rm -rf "$CONFIG"
            # The vault key in the Keychain, if one was stored there.
            security delete-generic-password -a trayauth -s trayauth-vault-key >/dev/null 2>&1 || true
            echo "Accounts deleted."
            ;;
        *)
            echo "Accounts kept."
            ;;
    esac
fi

echo
echo "TrayAuth has been uninstalled."
