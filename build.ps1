<#
.SYNOPSIS
    Builds TrayAuth, and optionally packages it into a Setup.exe.

.DESCRIPTION
    Generates the icon, runs the tests, and publishes a framework-dependent single-file exe.

    Output goes to %LOCALAPPDATA%\TrayAuth-build rather than a bin/ folder beside the source,
    because this project lives under OneDrive and there is no reason to sync build artefacts.
    The one exception is the installer, which lands in dist/ so it can be attached to a release.

.PARAMETER Installer
    Also compile packaging/TrayAuth.iss into dist/TrayAuth-Setup-<version>.exe. This is the
    artefact published to GitHub Releases and referenced by the winget manifest.

.PARAMETER SelfContained
    Bundle the .NET runtime into the exe (~70 MB instead of ~3 MB). Worth turning on for the
    installer you publish, so it runs on machines with no .NET 8 runtime.

.PARAMETER SkipTests
    Skip the test run. Not recommended - the RFC test vectors are what prove the codes are right.

.PARAMETER Version
    Version stamped into the installer and its filename.
#>
[CmdletBinding()]
param(
    [switch]$Installer,
    [switch]$SelfContained,
    [switch]$SkipTests,
    [string]$Version = '1.1.0'
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$solution = Join-Path $root 'TrayAuth.sln'
$appProject = Join-Path $root 'src\TrayAuth\TrayAuth.csproj'
$iconProject = Join-Path $root 'tools\MakeIcon\MakeIcon.csproj'
$iconPath = Join-Path $root 'assets\icon.ico'

$buildRoot = Join-Path $env:LOCALAPPDATA 'TrayAuth-build'
$publishDir = Join-Path $buildRoot 'publish'

Write-Host ''
Write-Host 'TrayAuth build' -ForegroundColor Cyan
Write-Host '--------------'

# --- prerequisite -------------------------------------------------------------------------

$sdk = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdk | Select-String -SimpleMatch '8.0')) {
    throw "The .NET 8 SDK was not found. Install it with:  winget install Microsoft.DotNet.SDK.8 --source winget"
}

# --- icon ---------------------------------------------------------------------------------

Write-Host ''
Write-Host '[1/4] Generating the icon...' -ForegroundColor Yellow
& dotnet run --project $iconProject --no-launch-profile -c Release -- $iconPath
if ($LASTEXITCODE -ne 0) { throw 'The icon could not be generated.' }

# --- tests --------------------------------------------------------------------------------

if ($SkipTests) {
    Write-Host ''
    Write-Host '[2/4] Tests skipped.' -ForegroundColor DarkYellow
}
else {
    Write-Host ''
    Write-Host '[2/4] Running tests...' -ForegroundColor Yellow
    & dotnet test $solution -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. The build has been stopped.' }
}

# --- publish ------------------------------------------------------------------------------

Write-Host ''
Write-Host '[3/4] Publishing...' -ForegroundColor Yellow

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

$publishArgs = @(
    'publish', $appProject,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-o', $publishDir,
    '--nologo'
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'The publish step failed.' }

# --- done ---------------------------------------------------------------------------------

$exe = Join-Path $publishDir 'TrayAuth.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe but it was not produced." }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ''
Write-Host "[4/4] Built TrayAuth.exe ($sizeMb MB)" -ForegroundColor Green
Write-Host "      $exe"

# --- installer ----------------------------------------------------------------------------

if ($Installer) {
    Write-Host ''
    Write-Host 'Compiling the installer...' -ForegroundColor Yellow

    # winget installs Inno per-user by default; the classic locations are checked too so this
    # works regardless of how it got onto the machine.
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $iscc) {
        throw "Inno Setup 6 was not found. Install it with:  winget install JRSoftware.InnoSetup --source winget"
    }

    $distDir = Join-Path $root 'dist'
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null

    & $iscc `
        "/DAppVersion=$Version" `
        "/DSourceDir=$publishDir" `
        (Join-Path $root 'packaging\TrayAuth.iss')

    if ($LASTEXITCODE -ne 0) { throw 'The installer failed to compile.' }

    $setup = Join-Path $distDir "TrayAuth-Setup-$Version.exe"
    if (-not (Test-Path $setup)) { throw "Expected $setup but it was not produced." }

    $setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 2)
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash

    Write-Host ''
    Write-Host "Installer: TrayAuth-Setup-$Version.exe ($setupMb MB)" -ForegroundColor Green
    Write-Host "  $setup"
    Write-Host "  SHA256  $hash"
}

Write-Host ''
if ($Installer) {
    Write-Host 'Next: attach dist\TrayAuth-Setup-*.exe to a GitHub release, then submit the winget manifest.'
}
else {
    Write-Host 'Next: run install.ps1 to install it, or just double-click Install.bat.'
    Write-Host 'For a distributable installer, run:  .\build.ps1 -Installer -SelfContained'
}
Write-Host ''
