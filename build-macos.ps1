<#
.SYNOPSIS
    Builds the macOS app bundles from Windows and packages them as tarballs.

.DESCRIPTION
    Cross-compiles src/TrayAuth.Desktop for osx-arm64 and/or osx-x64, assembles a proper
    TrayAuth.app bundle around each, and tars them into dist/.

    A .app is just a directory with a fixed shape, so it can be assembled anywhere:

        TrayAuth.app/Contents/
            Info.plist          - identity, LSUIElement (no Dock icon), version
            MacOS/trayauth      - the executable named by CFBundleExecutable
            Resources/*.icns    - the icon named by CFBundleIconFile

    Two things cannot be done from Windows and are handled on the Mac instead: the executable
    bit (install.sh chmods it) and code signing (unsigned; install.sh clears quarantine).

.PARAMETER Version
    Version stamped into Info.plist and the tarball names.

.PARAMETER Architecture
    arm64 (Apple Silicon), x64 (Intel), or both.
#>
[CmdletBinding()]
param(
    [string]$Version = '0.2.0-alpha',

    [ValidateSet('arm64', 'x64', 'both')]
    [string]$Architecture = 'both'
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\TrayAuth.Desktop\TrayAuth.Desktop.csproj'
$iconProject = Join-Path $root 'tools\MakeIcon\MakeIcon.csproj'
$icnsPath = Join-Path $root 'assets\trayauth.icns'
$icoPath = Join-Path $root 'assets\icon.ico'
$distDir = Join-Path $root 'dist'
$buildRoot = Join-Path $env:LOCALAPPDATA 'TrayAuth-build\macos'

$targets = switch ($Architecture) {
    'arm64' { @(@{ Rid = 'osx-arm64'; Label = 'arm64' }) }
    'x64'   { @(@{ Rid = 'osx-x64';   Label = 'x64' }) }
    default {
        @(
            @{ Rid = 'osx-arm64'; Label = 'arm64' },
            @{ Rid = 'osx-x64';   Label = 'x64' }
        )
    }
}

Write-Host ''
Write-Host 'TrayAuth macOS build (cross-compiled from Windows)' -ForegroundColor Cyan
Write-Host '--------------------------------------------------'

# --- icons ---------------------------------------------------------------------------------

Write-Host ''
Write-Host '[1/3] Generating icons...' -ForegroundColor Yellow

& dotnet run --project $iconProject --no-launch-profile -c Release -- $icoPath
if ($LASTEXITCODE -ne 0) { throw 'The .ico could not be generated.' }

& dotnet run --project $iconProject --no-launch-profile -c Release -- $icnsPath
if ($LASTEXITCODE -ne 0) { throw 'The .icns could not be generated.' }

# --- publish + bundle ----------------------------------------------------------------------

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
$plistTemplate = Get-Content (Join-Path $root 'packaging\macos\Info.plist') -Raw
$results = @()

foreach ($target in $targets) {
    $rid = $target.Rid
    $label = $target.Label

    Write-Host ''
    Write-Host "[2/3] Publishing $rid..." -ForegroundColor Yellow

    $publishDir = Join-Path $buildRoot "$rid\publish"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    # Not single-file: a .app bundle is already a container, and Apple's expectation is a
    # normal executable in Contents/MacOS alongside its libraries.
    & dotnet publish $project `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=none `
        -o $publishDir `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }

    $binary = Join-Path $publishDir 'trayauth'
    if (-not (Test-Path $binary)) { throw "Expected $binary but it was not produced." }

    Write-Host "      Assembling TrayAuth.app ($label)..." -ForegroundColor Yellow

    $stageName = "TrayAuth-macOS-$Version-$label"
    $stageDir = Join-Path $buildRoot "$rid\stage\$stageName"
    if (Test-Path (Split-Path $stageDir -Parent)) { Remove-Item (Split-Path $stageDir -Parent) -Recurse -Force }

    $appDir = Join-Path $stageDir 'TrayAuth.app'
    $contents = Join-Path $appDir 'Contents'
    $macOsDir = Join-Path $contents 'MacOS'
    $resources = Join-Path $contents 'Resources'

    New-Item -ItemType Directory -Path $macOsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $resources -Force | Out-Null

    Copy-Item (Join-Path $publishDir '*') $macOsDir -Recurse -Force
    Copy-Item $icnsPath (Join-Path $resources 'trayauth.icns') -Force

    # Info.plist must be LF-terminated and BOM-free for Apple's parser.
    $plist = $plistTemplate.Replace('__VERSION__', $Version).Replace("`r`n", "`n")
    [System.IO.File]::WriteAllText(
        (Join-Path $contents 'Info.plist'),
        $plist,
        (New-Object System.Text.UTF8Encoding($false)))

    # PkgInfo is legacy but harmless, and some tooling still looks for it.
    [System.IO.File]::WriteAllText(
        (Join-Path $contents 'PkgInfo'),
        'APPL????',
        (New-Object System.Text.UTF8Encoding($false)))

    foreach ($file in 'install.sh', 'uninstall.sh', 'README-MACOS.md') {
        $text = [System.IO.File]::ReadAllText((Join-Path $root "packaging\macos\$file")).Replace("`r`n", "`n")
        [System.IO.File]::WriteAllText(
            (Join-Path $stageDir $file),
            $text,
            (New-Object System.Text.UTF8Encoding($false)))
    }

    Write-Host "[3/3] Creating the tarball ($label)..." -ForegroundColor Yellow

    $tarball = Join-Path $distDir "$stageName.tar.gz"
    if (Test-Path $tarball) { Remove-Item $tarball -Force }

    # Windows' own bsdtar by full path: Git Bash's GNU tar reads "C:" as a remote host.
    & "$env:SystemRoot\System32\tar.exe" -czf $tarball -C (Split-Path $stageDir -Parent) $stageName
    if ($LASTEXITCODE -ne 0) { throw "tar failed for $label." }

    $results += [PSCustomObject]@{
        Arch   = $label
        File   = "$stageName.tar.gz"
        SizeMb = [math]::Round((Get-Item $tarball).Length / 1MB, 1)
        Sha256 = (Get-FileHash $tarball -Algorithm SHA256).Hash
    }
}

Write-Host ''
Write-Host 'Built:' -ForegroundColor Green
foreach ($r in $results) {
    Write-Host "  $($r.Arch.PadRight(6)) $($r.File)  ($($r.SizeMb) MB)"
    Write-Host "         SHA256 $($r.Sha256)"
}

Write-Host ''
Write-Host 'On the Mac:'
Write-Host '  tar xzf TrayAuth-macOS-*.tar.gz && cd TrayAuth-macOS-*/ && sh install.sh'
Write-Host '  ~/Applications/TrayAuth.app/Contents/MacOS/trayauth --selftest'
Write-Host ''
