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

## The PR body is not optional

Flathub's `submission-checker` bot runs hourly and **auto-closes any submission PR whose body
does not contain the completed template checklist**. Replacing the template with your own
description - however thorough - closes the PR with "Checklist(s) not completed or missing".

The checklist lives at `.github/pull_request_template.md` on the flathub/flathub master branch.
Read it fresh at submission time rather than trusting a copy here; it changes. As of the first
submission it required five ticks, and one of them is the reason a local build cannot be
skipped:

> Please attach a video showcasing the application on Linux using the Flatpak.

So the app must be built and run as a Flatpak, and recorded, before the PR can pass the bot -
the Flathub build bot is not a substitute for that.

## Lint is mandatory, and is not optional politeness

Both must pass before submitting - the same checks run on the PR:

```bash
flatpak run --command=flatpak-builder-lint org.flatpak.Builder manifest io.github.KlowdfurrRad.TrayAuth.yml
flatpak run --command=flatpak-builder-lint org.flatpak.Builder repo repo
```

Build with `--repo=repo` so the second command has a repository to inspect, and build using
`org.flatpak.Builder` rather than the distro's `flatpak-builder` - the latter invokes
`appstream-compose`, which no longer exists in the 24.08 runtime.

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
