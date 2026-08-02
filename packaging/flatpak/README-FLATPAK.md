# Building the TrayAuth Flatpak (on Ubuntu)

One-time setup. Note `org.flatpak.Builder` rather than the distro's `flatpak-builder`: Flathub
recommends it, and the distro package calls `appstream-compose`, which no longer exists in the
24.08 runtime, so it fails at the very last step of an otherwise successful build.

```bash
sudo apt install -y flatpak git
flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install --user -y flathub \
    org.flatpak.Builder \
    org.freedesktop.Platform//24.08 \
    org.freedesktop.Sdk//24.08 \
    org.freedesktop.Sdk.Extension.dotnet8//24.08
```

`org.freedesktop.Sdk.Extension.dotnet8` is the one the build actually depends on - it bolts the
.NET 8 compiler onto the SDK. dotnet9 or dotnet10 will not do: this is a .NET 8 app, and
`nuget-sources.json` pins .NET 8 runtime packs.

Build and install:

```bash
git clone https://github.com/KlowdfurrRad/TrayAuth.git   # or: git -C TrayAuth pull
cd TrayAuth/packaging/flatpak

flatpak run org.flatpak.Builder --user --install --force-clean --repo=repo \
    build io.github.KlowdfurrRad.TrayAuth.yml

flatpak run io.github.KlowdfurrRad.TrayAuth
```

`nuget-sources.json` is already committed here, so there is nothing to generate first.
(`generate-sources.sh` exists only for regenerating it on Linux after a dependency change;
the usual route is `tools/generate-nuget-sources.ps1` on the Windows side.)

## Lint before submitting

Flathub requires both of these to pass. `--repo=repo` above exists so the second one has
something to inspect.

```bash
flatpak run --command=flatpak-builder-lint org.flatpak.Builder manifest \
    io.github.KlowdfurrRad.TrayAuth.yml

flatpak run --command=flatpak-builder-lint org.flatpak.Builder repo repo
```

## What to check

Same list as the tarball, plus the sandbox specifics:

1. Tray icon appears; menu shows live codes; click copies; clipboard clears after 20 s
   (wl-copy is bundled inside the flatpak - no host package needed).
2. The vault key lands in the GNOME keyring (secret-tool is bundled too). `--selftest` works:
   `flatpak run io.github.KlowdfurrRad.TrayAuth --selftest`
3. Import your export file via the panel - file dialogs go through the portal.

## Flatpak-specific behaviour

- The flatpak keeps its vault in its own sandboxed config
  (`~/.var/app/io.github.KlowdfurrRad.TrayAuth/config/trayauth/`), separate from the tarball
  install's `~/.config/trayauth/`. Moving between the two = export, then import.
- "Start on login" is hidden inside the flatpak: the sandbox blocks direct autostart entries.
  The Background portal is the proper fix, planned. Until then GNOME Tweaks can add
  `flatpak run io.github.KlowdfurrRad.TrayAuth` as a startup command.
- On a pure Xorg session, tray-menu copying falls back to the panel clipboard (wl-copy needs
  a Wayland socket). Ubuntu's default Wayland session is fine.

## Flathub submission (after the local build passes)

1. A screenshot for the store listing: run the panel, screenshot it, save as
   `packaging/flatpak/screenshots/panel.png` in the repo.
2. The submission PR to github.com/flathub/flathub uses this manifest with the `dir` source
   replaced by a git tag + commit, plus the generated `nuget-sources.json` committed alongside.
3. After merge, Flathub builds it for x86_64 and aarch64 and it appears in App Center /
   GNOME Software; the external-data-checker bot can auto-PR future tags.
