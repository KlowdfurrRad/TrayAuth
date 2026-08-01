# Flathub submission files

What goes in the PR to [flathub/flathub](https://github.com/flathub/flathub), on a branch named
`io.github.KlowdfurrRad.TrayAuth`, at the repository root:

```
io.github.KlowdfurrRad.TrayAuth.yml     (this folder, with the tag/commit filled in)
nuget-sources.json                      (copied from ../nuget-sources.json)
```

The `PLACEHOLDER_TAG` / `PLACEHOLDER_COMMIT` values are substituted at submission time by
`../../../tools/prepare-flathub-pr.ps1`, which reads the real tag and its commit hash from the
repository. Flathub builds from an immutable commit, never a moving branch.

## Regenerating nuget-sources.json

Required whenever a package reference or the pinned `RuntimeFrameworkVersion` changes:

```powershell
.\tools\generate-nuget-sources.ps1
```

Flathub's builders have no network access, so every NuGet package must be listed with its URL
and SHA-512 up front. `RuntimeFrameworkVersion` is pinned in `TrayAuth.Desktop.csproj` so that the
closure resolved on the generating machine matches what Flathub's dotnet8 SDK extension asks
for; without that pin the two drift apart whenever either side's SDK patch level moves, and the
build fails with a missing package.

## After the PR is opened

1. The Flathub bot builds the manifest and comments with a test install command - that is the
   easiest way to try the real sandboxed build without a local flatpak-builder setup.
2. A human reviewer checks the manifest, permissions and AppStream metadata. Expect questions
   about anything unusual; ours are the two bundled tools (`wl-copy`, `secret-tool`) and the
   Wayland socket, all explained in comments in the manifest itself.
3. On merge, Flathub creates `flathub/io.github.KlowdfurrRad.TrayAuth`, builds for x86_64 and
   aarch64, and the app appears in App Center / GNOME Software within a few hours.
4. Future releases: push a tag, then update the tag/commit (and `nuget-sources.json` if
   dependencies changed) in that repository.
