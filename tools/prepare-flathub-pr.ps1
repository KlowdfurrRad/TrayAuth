<#
.SYNOPSIS
    Stages the two files that make up the Flathub submission.

.DESCRIPTION
    Resolves a git tag to its immutable commit, substitutes it into the Flathub manifest, and
    copies that plus nuget-sources.json into an output folder ready to commit into a clone of
    flathub/flathub on a branch named after the app id.

    Flathub builds from a commit, never a branch, so the tag must already be pushed.

.PARAMETER Tag
    The git tag to submit, e.g. linux-v0.1.0-alpha.

.PARAMETER OutputDir
    Where to write the staged files. Defaults to dist/flathub-submission.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$flatpakDir = Join-Path $root 'packaging\flatpak'

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root 'dist\flathub-submission'
}

# Resolve the tag to the commit it points at, failing loudly if it does not exist.
$commit = (& git -C $root rev-list -n 1 $Tag 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
    throw "Tag '$Tag' not found. Create and push it first: git tag $Tag && git push origin $Tag"
}

$commit = $commit.Trim()

$onRemote = (& git -C $root ls-remote --tags origin "refs/tags/$Tag" 2>$null)
if ([string]::IsNullOrWhiteSpace($onRemote)) {
    throw "Tag '$Tag' exists locally but not on origin. Push it: git push origin $Tag"
}

Write-Host ''
Write-Host "Tag    $Tag"
Write-Host "Commit $commit"

$sourcesPath = Join-Path $flatpakDir 'nuget-sources.json'
if (-not (Test-Path $sourcesPath)) {
    throw "nuget-sources.json is missing. Run tools\generate-nuget-sources.ps1 first."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$manifest = Get-Content (Join-Path $flatpakDir 'flathub\io.github.KlowdfurrRad.TrayAuth.yml') -Raw
$manifest = $manifest.Replace('PLACEHOLDER_TAG', $Tag).Replace('PLACEHOLDER_COMMIT', $commit)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText(
    (Join-Path $OutputDir 'io.github.KlowdfurrRad.TrayAuth.yml'),
    $manifest.Replace("`r`n", "`n"),
    $utf8NoBom)

Copy-Item $sourcesPath (Join-Path $OutputDir 'nuget-sources.json') -Force

Write-Host ''
Write-Host "Staged in $OutputDir" -ForegroundColor Green
Get-ChildItem $OutputDir | ForEach-Object { "  $($_.Name)" }

Write-Host ''
Write-Host 'To submit:'
Write-Host '  1. Fork github.com/flathub/flathub'
Write-Host '  2. git clone your fork; git checkout -b io.github.KlowdfurrRad.TrayAuth'
Write-Host "  3. Copy both files from $OutputDir into the repository root"
Write-Host '  4. Commit, push, open the PR against flathub/flathub (branch: new-pr)'
Write-Host ''
