# TrayAuth for Linux (alpha)

Two-factor authentication codes in your system tray. This is the Linux sibling of
[TrayAuth for Windows](https://github.com/KlowdfurrRad/TrayAuth) - same vault format, same
export files, same RFC-verified code generation.

## Install

```bash
tar xzf TrayAuth-Linux-*.tar.gz
cd TrayAuth-Linux-*/
./install.sh
```

No sudo - everything goes under `~/.local`. Then, **before anything else**:

```bash
trayauth --selftest
```

This proves the vault crypto, file permissions, RFC test vectors and export/import on your
machine, with no GUI involved. It must end with `SELFTEST OK`. If it doesn't, please report the
full output.

Then start it:

```bash
trayauth &
```

Recommended packages:

```bash
sudo apt install wl-clipboard      # copying from the tray menu on Wayland
sudo apt install libsecret-tools   # keeps the vault key in the GNOME keyring
```

Without `libsecret-tools` the key falls back to a 0600 file next to the vault (weaker; the
selftest tells you which one is in use). Without `wl-clipboard`, copying works only from the
panel while it has focus - Wayland does not let background apps take the clipboard.

## Using it

- **Left-click** the tray icon: the codes panel.
- **Right-click** the tray icon: every account with its live code - click to copy. The
  clipboard clears itself after 20 seconds.
- Bring accounts over from Windows TrayAuth: export there, then **Import file** here. Google
  Authenticator transfer QRs import via **Import QR** (screenshot the QR on the phone first).

## Moving your accounts from Windows

On Windows: tray menu → *Export all accounts…* → copy the folder over → here: **Import file** →
pick `TrayAuth-export.json`. Codes will match the Windows app digit-for-digit.

## Known alpha limitations

- No slide-out animation and no positioning next to the tray icon - Wayland does not allow
  apps to place their own windows. The panel opens centered.
- No global hotkey yet (needs the GlobalShortcuts portal; planned).
- No screen-scan QR import yet (needs the Screenshot portal; planned) - use image files.
- On stock GNOME (not Ubuntu), tray icons need the AppIndicator extension. Ubuntu ships it
  enabled by default.

## Uninstall

```bash
./uninstall.sh
```

Your encrypted accounts in `~/.config/trayauth` are kept unless you say otherwise.
