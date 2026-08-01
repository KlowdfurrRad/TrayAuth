<#
.SYNOPSIS
    Builds the Linux app from Windows and packages it as a tarball.

.DESCRIPTION
    Cross-compiles src/TrayAuth.Desktop for linux-x64 (self-contained single file - no .NET
    needed on the target), stages it with the install scripts from packaging/linux, and tars
    the lot into dist/. No WSL, no Docker: plain dotnet cross-compilation.

    Note: a tar created on Windows does not carry the executable bit; install.sh runs
    `install -m 755` precisely so that never matters.

.PARAMETER Version
    Version stamped into the tarball name.
#>
[CmdletBinding()]
param(
    [string]$Version = '0.2.0-alpha'
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\TrayAuth.Desktop\TrayAuth.Desktop.csproj'
$publishDir = Join-Path $env:LOCALAPPDATA 'TrayAuth-build\linux-publish'
$stageName = "TrayAuth-Linux-$Version"
$stageRoot = Join-Path $env:LOCALAPPDATA 'TrayAuth-build\linux-stage'
$stageDir = Join-Path $stageRoot $stageName
$distDir = Join-Path $root 'dist'

Write-Host ''
Write-Host 'TrayAuth Linux build (cross-compiled from Windows)' -ForegroundColor Cyan
Write-Host '---------------------------------------------------'

Write-Host ''
Write-Host '[1/3] Publishing linux-x64...' -ForegroundColor Yellow

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& dotnet publish $project `
    -c Release `
    -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $publishDir `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$binary = Join-Path $publishDir 'trayauth'
if (-not (Test-Path $binary)) { throw "Expected $binary but it was not produced." }

Write-Host ''
Write-Host '[2/3] Staging...' -ForegroundColor Yellow

if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

Copy-Item $binary (Join-Path $stageDir 'trayauth')
foreach ($file in 'install.sh', 'uninstall.sh', 'trayauth.desktop', 'trayauth.svg', 'README-LINUX.md') {
    Copy-Item (Join-Path $root "packaging\linux\$file") (Join-Path $stageDir $file)
}

Write-Host ''
Write-Host '[3/3] Creating the tarball...' -ForegroundColor Yellow

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
$tarball = Join-Path $distDir "$stageName-linux-x64.tar.gz"
if (Test-Path $tarball) { Remove-Item $tarball -Force }

# Windows' own bsdtar, by full path: Git Bash puts GNU tar on PATH, which reads "C:" as a
# remote host name and fails.
& "$env:SystemRoot\System32\tar.exe" -czf $tarball -C $stageRoot $stageName
if ($LASTEXITCODE -ne 0) { throw 'tar failed.' }

$sizeMb = [math]::Round((Get-Item $tarball).Length / 1MB, 1)
$hash = (Get-FileHash $tarball -Algorithm SHA256).Hash

Write-Host ''
Write-Host "Tarball: $stageName-linux-x64.tar.gz ($sizeMb MB)" -ForegroundColor Green
Write-Host "  $tarball"
Write-Host "  SHA256  $hash"
Write-Host ''
Write-Host 'On the Ubuntu machine:'
Write-Host "  tar xzf $stageName-linux-x64.tar.gz && cd $stageName && bash install.sh"
Write-Host '  ~/.local/bin/trayauth --selftest'
Write-Host ''
