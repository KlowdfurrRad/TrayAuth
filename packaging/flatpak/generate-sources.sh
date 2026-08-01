#!/bin/sh
# Regenerates nuget-sources.json - the pinned list of every NuGet package the flatpak build
# needs, with URLs and hashes, because Flathub builds run with no network access.
#
# YOU PROBABLY DO NOT NEED THIS. nuget-sources.json is committed to the repository and is
# regenerated on the Windows side by tools/generate-nuget-sources.ps1. Run this only if you
# have changed a package reference or the pinned RuntimeFrameworkVersion and cannot use the
# PowerShell tool.
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
GENERATOR_URL="https://raw.githubusercontent.com/flatpak/flatpak-builder-tools/master/dotnet/flatpak-dotnet-generator.py"
GENERATOR=/tmp/flatpak-dotnet-generator.py

echo "Fetching the Flathub dotnet generator..."
if ! curl -fsSL "$GENERATOR_URL" -o "$GENERATOR"; then
    echo >&2
    echo "Could not download the generator from:" >&2
    echo "  $GENERATOR_URL" >&2
    echo >&2
    echo "The upstream layout may have moved. This script is optional:" >&2
    echo "nuget-sources.json is already committed next to this script, so you can go" >&2
    echo "straight to the build:" >&2
    echo >&2
    echo "  flatpak-builder --user --install --force-clean build io.github.KlowdfurrRad.TrayAuth.yml" >&2
    echo >&2
    exit 1
fi

echo "Resolving the package closure (runs dotnet restore inside the SDK extension)..."
python3 "$GENERATOR" \
    --dotnet 8 \
    --freedesktop 24.08 \
    --runtime linux-x64 \
    "$HERE/nuget-sources.json" \
    "$HERE/../../src/TrayAuth.Desktop/TrayAuth.Desktop.csproj"

echo
echo "Wrote $HERE/nuget-sources.json"
echo "Next: flatpak-builder --user --install --force-clean build io.github.KlowdfurrRad.TrayAuth.yml"
