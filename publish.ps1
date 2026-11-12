# Builds a self-contained, single-file Windows x64 release of the overlay and zips it.
# Usage:  ./publish.ps1            (version defaults to 'dev' -> the exe keeps the csproj <Version>)
#         ./publish.ps1 v0.1.0     (bakes 0.1.0 into the exe, so the zip name and the binary agree)
param([string]$Version = "dev")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = "$root/publish"
$zip = "$root/POE2Radar-$Version-win-x64.zip"

# The zip is NAMED after $Version, so the binary must carry the same version (UpdateChecker compares the
# baked-in assembly version to the latest release). 'vX.Y.Z' / 'X.Y.Z' is passed to the build exactly like
# the release workflow does; 'dev' builds whatever the csproj says; anything else is refused rather than
# producing a zip whose label and contents disagree.
$versionArgs = @()
$number = $Version -replace '^[vV]', ''
if ($number -match '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.\-]*)?$') {
    $versionArgs = @("-p:Version=$number")
}
elseif ($Version -ne "dev") {
    throw "Version '$Version' is not of the form vX.Y.Z (or 'dev'). Refusing to build a mislabelled zip."
}

# Start from an empty output folder: a previous build's files - or the config/ and icons/ folders the exe
# creates next to itself if it was ever run from publish/ - must never end up in the zip.
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

dotnet publish "$root/src/POE2Radar.Overlay/POE2Radar.Overlay.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -p:Deterministic=true -p:ContinuousIntegrationBuild=true `
    @versionArgs `
    -o $out

# $ErrorActionPreference does not apply to native commands: without this check a failed build would fall
# through and zip whatever was lying around.
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE) - nothing was packaged." }

# Belt-and-suspenders: never ship .pdb (they embed the build path / dev username).
Remove-Item "$out/*.pdb" -Force -ErrorAction SilentlyContinue

Copy-Item "$root/README.md", "$root/LICENSE" "$out/" -Force
Compress-Archive -Path "$out/*" -DestinationPath $zip -Force
Write-Host "Built: $zip"
