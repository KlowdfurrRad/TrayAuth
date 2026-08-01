#!/bin/sh
# Removes TrayAuth. Your accounts in ~/.config/trayauth are kept unless you delete them here.
# POSIX sh, runnable as `sh uninstall.sh` - see install.sh for why.
set -eu

echo
echo "Uninstalling TrayAuth"
echo "---------------------"

pkill -x trayauth 2>/dev/null || true
sleep 1

rm -f "$HOME/.local/bin/trayauth"
rm -rf "$HOME/.local/share/trayauth"
rm -f "$HOME/.local/share/applications/trayauth.desktop"
rm -f "$HOME/.local/share/icons/hicolor/scalable/apps/trayauth.svg"
rm -f "$HOME/.config/autostart/trayauth.desktop"

echo "Program removed."
echo

if [ -d "$HOME/.config/trayauth" ]; then
    echo "Your accounts are still at ~/.config/trayauth (encrypted)."
    printf "Delete them too? They cannot be recovered afterwards. [y/N] "
    read -r answer || answer=""
    case "$answer" in
        [Yy]*)
            rm -rf "$HOME/.config/trayauth"
            # The vault key in the keyring, if one was stored there.
            command -v secret-tool >/dev/null 2>&1 \
                && secret-tool clear application trayauth type vault-key 2>/dev/null || true
            echo "Accounts deleted."
            ;;
        *)
            echo "Accounts kept."
            ;;
    esac
fi

echo
echo "TrayAuth has been uninstalled."
