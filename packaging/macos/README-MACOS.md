# TrayAuth for macOS (alpha)

Two-factor authentication codes in the macOS menu bar. Shares its entire tested core with the
Windows and Linux builds - same code generation, same vault format, same export files - so
accounts move between platforms by export then import.

**This build has never run on a Mac.** It is cross-compiled and its shared logic is verified,
but every macOS-specific surface (menu bar icon, Keychain, clipboard, start-on-login) is
unproven until you run it. Please report anything odd.

## Install

Download the archive for your chip - `arm64` for Apple Silicon (M1 and later), `x64` for Intel:

```bash
tar xzf TrayAuth-macOS-*-arm64.tar.gz
cd TrayAuth-macOS-*-arm64
sh install.sh
```

The installer copies `TrayAuth.app` into `~/Applications`, restores the executable bit, and
clears the download quarantine flag. Then, before anything else:

```bash
~/Applications/TrayAuth.app/Contents/MacOS/trayauth --selftest
```

It must end with `SELFTEST OK`. Then:

```bash
open ~/Applications/TrayAuth.app
```

Look for the keyhole icon in the menu bar, near the clock. **TrayAuth has no Dock icon** - it is
a menu bar app (`LSUIElement`), like the Windows version has no taskbar button.

## About Gatekeeper

The app is not signed or notarized (that needs a US$99/year Apple Developer account). Downloaded
unsigned apps are blocked by Gatekeeper on first launch. `install.sh` clears the quarantine
attribute for you, which is what right-click → Open does behind the scenes.

If you ever see *"cannot be opened because the developer cannot be verified"*, either:

- right-click the app → **Open** → **Open** again, or
- `xattr -dr com.apple.quarantine ~/Applications/TrayAuth.app`

## Using it

- **Click the menu bar icon** for the codes panel.
- The menu lists every account with its live code - click one to copy. The clipboard clears
  itself after 20 seconds.
- **Start on login** installs a launchd LaunchAgent at
  `~/Library/LaunchAgents/io.github.klowdfurrrad.trayauth.plist`.
- Import accounts from a Windows or Linux TrayAuth export, or from Google Authenticator
  transfer QR screenshots.

## Where things live

| | |
|---|---|
| Accounts | `~/Library/Application Support/TrayAuth/vault.dat` (AES-256-GCM) |
| Vault key | macOS Keychain, item `trayauth-vault-key` |
| App | `~/Applications/TrayAuth.app` |
| Start on login | `~/Library/LaunchAgents/io.github.klowdfurrrad.trayauth.plist` |

The key sits in the Keychain, which plays the same role DPAPI does on Windows: bound to your
login, unreadable by other accounts. If the Keychain cannot be reached, TrayAuth falls back to
a `0600` key file beside the vault and says so in `--selftest`.

## Known alpha limitations

- No global hotkey yet.
- QR import reads image files only (no screen scanning).
- The menu bar icon is the app's colour icon rather than a monochrome template image, so it
  will not adapt to light/dark menu bars the way native icons do.

## Uninstall

```bash
sh uninstall.sh
```

Your accounts are kept unless you choose to delete them.
