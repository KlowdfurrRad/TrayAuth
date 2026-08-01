#!/bin/sh
# Generates nuget-sources.json - the pinned list of every NuGet package the flatpak build
# needs, with URLs and hashes, because Flathub builds run with no network access.
#
# Run on a machine with flatpak + the dotnet8 SDK extension installed (see README-FLATPAK.md).
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
GENERATOR_URL="https://raw.githubusercontent.com/flathub/flatpak-builder-tools/master/dotnet/flatpak-dotnet-generator.py"
GENERATOR=/tmp/flatpak-dotnet-generator.py

echo "Fetching the Flathub dotnet generator..."
curl -fsSL "$GENERATOR_URL" -o "$GENERATOR"

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
