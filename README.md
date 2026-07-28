# TrayAuth

Google Authenticator, but in the Windows taskbar. Click the tray icon and a panel slides up out of
the taskbar with your live codes; click a code to copy it.

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
| Copy a code | Click it. The clipboard clears itself 20 seconds later |
| Add an account | The **+** button, or **Add account** at the bottom |
| Edit / export / delete one | Right-click its row |
| Export everything | Tray menu → **Export all accounts…** |
| Restore a backup | Tray menu → **Import…** |
| Close the panel | Click anywhere else, or press **Esc** |

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
src/TrayAuth/
  Core/       Base32, Totp, Account, Vault, ExportService, ClipboardService, StartupRegistration
  Interop/    TaskbarInfo (taskbar edge), HotKey, native declarations
  UI/         TrayContext, PanelForm (the slide), AccountRow, AddAccountDialog, Theme, AppIcon
tests/        89 tests
tools/MakeIcon/   generates assets/icon.ico
packaging/    TrayAuth.iss - the Inno Setup installer
```

The code generator is plain `System.Security.Cryptography` against RFC 4226/6238, and the tests
assert the published RFC 6238 vectors for SHA1, SHA256 and SHA512. If those pass, TrayAuth agrees
with every other authenticator in existence.

## What it deliberately doesn't do

- **Scan QR codes to add accounts.** You type the setup key. QR is export-only.
- **Ask for a master password.** DPAPI unlocks with your Windows login, so the panel opens
  instantly. The trade-off: anything already running as you can read the vault.
- **Sync anything anywhere.** No account, no server, no telemetry. Backups are files you move.
