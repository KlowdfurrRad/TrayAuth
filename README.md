# TrayAuth

Two-factor authentication codes in the Windows taskbar. Click the tray icon and a panel slides up
out of the taskbar with your live codes; click a code to copy it.

Accounts are added by typing the **setup key** — the base32 string a site shows next to its QR
image, usually behind a "can't scan the code?" link.

```
                                      ┌────────────────────────────┐
                                      │  Authenticator      +   ⋯  │
                                      ├────────────────────────────┤
                                      │  GitHub · you@example.com  │
                                      │  482 913              ◜21◝ │
                                      │                            │
                                      │  AWS · root                │
                                      │  105 774              ◜21◝ │
                                      ├────────────────────────────┤
                                      │  +  Add account            │
                                      └────────────────────────────┘
  ──────────────────────────────────────────────────────────[🔐]──────
                                                        tray icon ▲
```

## Install

```powershell
winget install KlowdfurrRad.TrayAuth
```

Or download `TrayAuth-Setup-*.exe` from the
[Releases page](https://github.com/KlowdfurrRad/TrayAuth/releases) and run it.

Either way you get a Start Menu entry, an entry in Settings → Installed apps, and start-with-Windows
so the icon is always in the tray. **No administrator rights and no UAC prompt** — everything lives
under `HKCU` and `%LOCALAPPDATA%`. The installer bundles the .NET runtime, so there is nothing else
to install.

To remove it, use Settings → Installed apps. Your accounts are kept unless you say otherwise when
asked.

## Using it

| | |
|---|---|
| Open the panel | Click the tray icon, or press **Ctrl+Alt+A** |
| Copy a code | Click it in the panel — or **right-click the tray icon** and click it there, no panel needed. Either way the clipboard clears itself 20 seconds later |
| Add an account | The **+** button, or **Add account** at the bottom |
| Edit / export / delete one | Right-click its row |
| Export everything | Tray menu → **Export all accounts…** |
| Restore a backup | Tray menu → **Import** → **From export file…** |
| Move in from Google Authenticator | Phone: *Settings → Transfer accounts → Export accounts*, screenshot the QR, get it to this PC, then tray menu → **Import** → **From QR image…** — or show the QR on this screen and use **Scan screen for QR code** |
| Copy from the desktop | Right-click the desktop → **TrayAuth codes** (on Windows 11 it lives under **Show more options**, or press Shift+F10). Click an account to copy its code |
| Close the panel | Click anywhere else, or press **Esc** |

The right-click menu shows every account with its live code and seconds remaining, so day-to-day
copying never needs the panel at all.

The desktop menu works the same way: the labels show live codes (TrayAuth refreshes them as codes
roll over), and clicking computes a fresh code at that instant — a label that went stale while the
menu sat open can never produce a stale copy. The codes are visible to anyone looking at your
unlocked screen, exactly like the tray menu; if you'd rather not have that, turn it off with tray
menu → **Codes in desktop right-click menu**.

QR import understands both kinds of QR: a site's ordinary `otpauth://` enrollment QR, and Google
Authenticator's transfer QR (`otpauth-migration://`), which can carry many accounts at once. If a
transfer spans several QRs, scan them together or one after another — TrayAuth tells you when part
of the set is still missing. Counter-based (HOTP) entries are skipped and reported by name-count,
never silently.

The clipboard auto-clear only removes the code if the clipboard still contains it, so anything you
copy in the meantime is left alone.

### Adding an account

Fill in the issuer (the service), your account name there, and the setup key. Case, spaces and
hyphens in the key don't matter — paste it however the site prints it.

The dialog shows the code your key produces as you type. **Check it matches the code the site is
showing before you save.** That is the whole point of the preview: a mistyped key that you only
discover later means turning 2FA off and on again at that site.

Most services use the defaults (6 digits, 30 seconds, SHA1). If yours doesn't, open **Advanced**.

## Backups — read this one

Your accounts are encrypted with your Windows user account (DPAPI). That means the vault file is
useless to anyone who copies it, and unreadable by any other Windows profile.

It also means **a DPAPI vault dies with the Windows profile that wrote it**. Reinstall Windows, lose
the profile, or move to a new PC, and `vault.dat` cannot be read again — by you or anyone.

So export your accounts:

> Tray menu → **Export all accounts…**

You get a folder like this:

```
TrayAuth-export-2026-07-28-1430/
  TrayAuth-export.json              every account — import this to restore everything
  READ ME - keep these files safe.txt
  GitHub - you@example.com.json     one account, same importable format
  GitHub - you@example.com.png      QR code — scan it with the app on your phone
  AWS - root.json
  AWS - root.png
```

The `.png` files are ordinary QR codes. Scanning one with Google Authenticator, Authy or 1Password
adds that account there — which is a good second copy to keep, independent of this machine.

**The exported files are not encrypted.** Each one holds the secret that mints that account's codes
forever, so treat the folder like the passwords themselves: keep it off cloud sync and shared
drives, and delete it once you have put it somewhere you trust.

## Where things live

| | |
|---|---|
| Accounts | `%APPDATA%\TrayAuth\vault.dat` — DPAPI-encrypted, ACL'd to you |
| Program | `%LOCALAPPDATA%\Programs\TrayAuth` |
| Default export folder | `%LOCALAPPDATA%\TrayAuth\exports` — local, deliberately not OneDrive |
| Build output | `%LOCALAPPDATA%\TrayAuth-build` |

If the vault is ever unreadable, TrayAuth renames it to `vault.dat.bad`, tells you, and starts
empty — it never deletes it, in case the failure turns out to be recoverable.

## Linux and macOS (alpha)

`src/TrayAuth.Desktop` is one Avalonia app covering both: a tray / menu-bar icon whose menu carries
live codes, the same vault document sealed with AES-256-GCM, and the same export files — so accounts
move between all three platforms by export → import, and the codes match digit-for-digit.

The vault key lives in whatever each OS offers: the **macOS Keychain** via `security`, the **GNOME
keyring** via `secret-tool` on Linux, with a `0600` key-file fallback if neither answers. All logic
is shared with Windows through `TrayAuth.Core` and its 103 portable tests.

### macOS

Download `TrayAuth-macOS-*-arm64.tar.gz` (Apple Silicon) or `-x64` (Intel):

```bash
tar xzf TrayAuth-macOS-*.tar.gz && cd TrayAuth-macOS-*/
sh install.sh                                                  # into ~/Applications, clears quarantine
~/Applications/TrayAuth.app/Contents/MacOS/trayauth --selftest # must print SELFTEST OK
open ~/Applications/TrayAuth.app
```

It's a menu-bar app (`LSUIElement`) — no Dock icon, by design. Unsigned, so `install.sh` clears the
download quarantine flag; without that Gatekeeper blocks the first launch. Details:
[`packaging/macos/README-MACOS.md`](packaging/macos/README-MACOS.md).

### Linux

Grab `TrayAuth-Linux-*.tar.gz` from Releases, then:

```bash
tar xzf TrayAuth-Linux-*.tar.gz && cd TrayAuth-Linux-*/
bash install.sh                     # per-user, no sudo (bash, not ./ - Windows-built tar has no exec bits)
~/.local/bin/trayauth --selftest    # must print SELFTEST OK
~/.local/bin/trayauth &
```

Recommended: `sudo apt install wl-clipboard libsecret-tools` (tray-menu copying on Wayland, and
keyring key storage).

Honest limitations on both: no slide animation or panel positioning (Wayland forbids apps placing
their own windows), no global hotkey yet, QR import from image files only. `build-linux.ps1` and
`build-macos.ps1` cross-compile the artifacts from Windows.

### Flatpak

A sandboxed Flatpak build lives in [`packaging/flatpak/`](packaging/flatpak/) (Flathub submission
planned — once it's live, this becomes `flatpak install flathub io.github.KlowdfurrRad.TrayAuth`).
To build it yourself on Ubuntu:

```bash
# one-time setup
sudo apt install -y flatpak flatpak-builder git python3 curl
flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install --user -y flathub org.freedesktop.Platform//24.08 \
    org.freedesktop.Sdk//24.08 org.freedesktop.Sdk.Extension.dotnet8//24.08

# build, install, run
cd packaging/flatpak
bash generate-sources.sh    # pins every NuGet package - Flathub builds run offline
flatpak-builder --user --install --force-clean build io.github.KlowdfurrRad.TrayAuth.yml
flatpak run io.github.KlowdfurrRad.TrayAuth
```

The sandbox bundles its own `wl-copy` and `secret-tool` (host binaries are invisible inside), asks
for no filesystem access (file dialogs go through the portal), and keeps its vault in the app's own
sandboxed config — move accounts between the flatpak and the tarball install via export → import.
"Start on login" is hidden inside the flatpak until the Background portal is wired up. Details and
the verification checklist: [`packaging/flatpak/README-FLATPAK.md`](packaging/flatpak/README-FLATPAK.md).

## Building from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.8`),
plus [Inno Setup](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`) if you
want to build the installer.

```powershell
.\build.ps1                              # icon, tests, then publish
.\build.ps1 -Installer -SelfContained    # also package dist\TrayAuth-Setup-<version>.exe
.\build.ps1 -SkipTests

dotnet test                              # tests on their own
dotnet run --project src\TrayAuth        # run without installing
```

`build.ps1` refuses to publish if the tests fail. `Install.bat` does the whole thing —
build, package, install — in one double-click.

### Layout

```
src/TrayAuth.Core/      shared by every platform: Base32, Totp, Account, OtpAuthUri,
                        GoogleAuthMigration (transfer-QR protobuf), QrImport, ExportService,
                        VaultDocument, FileProtection
src/TrayAuth/           Windows (WinForms): DPAPI Vault, QrDecoder, ClipboardService,
                        DesktopContextMenu, StartupRegistration, Interop/, UI/
src/TrayAuth.Desktop/   Linux + macOS (Avalonia): LocalVault (AES-GCM), VaultKeyStore
                        (Keychain / Secret Service / key file), ClipboardHelper, Autostart, UI/
tests/                  126 tests - 103 portable, 23 Windows-only
tools/MakeIcon/         generates assets/icon.ico and assets/trayauth.icns
packaging/              TrayAuth.iss (Inno), linux/, macos/, flatpak/
```

The code generator is plain `System.Security.Cryptography` against RFC 4226/6238, and the tests
assert the published RFC 6238 vectors for SHA1, SHA256 and SHA512. If those pass, TrayAuth agrees
with every other authenticator in existence.

## What it deliberately doesn't do

- **Use a webcam.** QR import reads image files and your screen, not a camera — screenshot the
  QR instead.
- **Import HOTP.** Counter-based entries have no clock to agree on and TrayAuth doesn't track
  counters; they are skipped with a note rather than imported wrong.
- **Ask for a master password.** DPAPI unlocks with your Windows login, so the panel opens
  instantly. The trade-off: anything already running as you can read the vault.
- **Sync anything anywhere.** No account, no server, no telemetry. Backups are files you move.
