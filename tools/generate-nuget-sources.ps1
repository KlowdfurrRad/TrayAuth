<#
.SYNOPSIS
    Generates packaging/flatpak/nuget-sources.json from Windows.

.DESCRIPTION
    Flathub builds run with no network, so every NuGet package the build will ask for must be
    listed up front with a URL and hash. The official Flathub generator needs flatpak (Linux);
    this produces the identical format by running the restore locally:

      1. dotnet restore -r linux-x64 into a clean packages folder - the full closure,
         including the linux-x64 runtime/apphost packs;
      2. hash every .nupkg and emit flatpak "file" sources pointing at nuget.org.

    Determinism note: the runtime-pack version is pinned via RuntimeFrameworkVersion in
    TrayAuth.Linux.csproj, so the closure resolved here matches what Flathub's dotnet8 SDK
    extension will request, regardless of SDK patch drift between the two machines.
#>
[CmdletBinding()]
param(
    # Defaulted in the body, not here: $PSScriptRoot is not populated in param defaults on
    # Windows PowerShell 5.1, which silently sends the output to the filesystem root.
    [string]$Output
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $PSScriptRoot '..\packaging\flatpak\nuget-sources.json'
}

$project = Join-Path $PSScriptRoot '..\src\TrayAuth.Linux\TrayAuth.Linux.csproj'
$packages = Join-Path $env:LOCALAPPDATA 'TrayAuth-build\nuget-closure'

Write-Host 'Resolving the package closure (clean restore, linux-x64)...'

if (Test-Path $packages) { Remove-Item $packages -Recurse -Force }
New-Item -ItemType Directory -Path $packages -Force | Out-Null

& dotnet restore $project -r linux-x64 --packages $packages -p:Configuration=Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

Write-Host 'Hashing packages...'

$entries = @()
foreach ($idDir in Get-ChildItem $packages -Directory) {
    foreach ($versionDir in Get-ChildItem $idDir.FullName -Directory) {
        $nupkg = Get-ChildItem $versionDir.FullName -Filter '*.nupkg' | Select-Object -First 1
        if ($null -eq $nupkg) { continue }

        $id = $idDir.Name
        $version = $versionDir.Name

        $entries += [ordered]@{
            type            = 'file'
            url             = "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg"
            sha512          = (Get-FileHash $nupkg.FullName -Algorithm SHA512).Hash.ToLowerInvariant()
            dest            = 'nuget-sources'
            'dest-filename' = "$id.$version.nupkg"
        }
    }
}

$entries = $entries | Sort-Object { $_.'dest-filename' }
$json = ConvertTo-Json @($entries) -Depth 4

# Normalise the ".." in the default path before writing, and write UTF-8 without a BOM -
# flatpak-builder's JSON parser will not accept a BOM.
$fullOutput = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($fullOutput)) -Force | Out-Null

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($fullOutput, $json + "`n", $utf8NoBom)

Write-Host ''
Write-Host "Wrote $($entries.Count) package sources to $fullOutput" -ForegroundColor Green