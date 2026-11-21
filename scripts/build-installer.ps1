# Packs a verified publish folder (from scripts\publish-windows.ps1) into the Velopack installer and
# checks that the installer really contains that publish's front-end.
#
# Usage:  .\scripts\build-installer.ps1 -PublishDir dist\KZCompanion-win-x64-rc -Version 1.0.0 [-OutDir dist\installer-1.0.0]
# Needs the Velopack CLI:  dotnet tool install -g vpk --version 1.2.161
#
# Output (in OutDir): KZCompanion-Setup-<version>.exe plus the update files the installed app downloads
# (KZCompanionPC-<version>-full.nupkg, releases.win.json, assets.win.json, RELEASES).
param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$Version,
    [string]$OutDir = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($PublishDir)) { $PublishDir = Join-Path $root $PublishDir }
if (-not $OutDir) { $OutDir = Join-Path $root "dist\installer-$Version" }
elseif (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $root $OutDir }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.0.0, got '$Version'" }
if (-not (Test-Path (Join-Path $PublishDir "KZCompanion.exe"))) { throw "$PublishDir has no KZCompanion.exe" }
if (-not (Test-Path (Join-Path $PublishDir "wwwroot\main.dart.js"))) { throw "$PublishDir has no embedded web front-end" }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw "vpk not found. Run: dotnet tool install -g vpk --version 1.2.161" }

# The exe inside must carry the version we are about to publish (a stale publish folder would not).
$fv = (Get-Item (Join-Path $PublishDir "KZCompanion.exe")).VersionInfo
if (-not $fv.ProductVersion.StartsWith($Version)) { throw "KZCompanion.exe is version '$($fv.ProductVersion)', expected $Version. Re-run publish-windows.ps1 -Version $Version" }

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
$icon = Join-Path $root "DroidDeck\kzcompanion.ico"
vpk pack --packId KZCompanionPC --packVersion $Version --packDir $PublishDir `
    --runtime win-x64 --mainExe KZCompanion.exe --packTitle "KZ Companion" --packAuthors "KZ Dev Studio" `
    --icon $icon --outputDir $OutDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }

$setup = Join-Path $OutDir "KZCompanionPC-win-Setup.exe"
if (-not (Test-Path $setup)) { throw "vpk did not produce $setup" }
Move-Item $setup (Join-Path $OutDir "KZCompanion-Setup-$Version.exe")
Remove-Item (Join-Path $OutDir "KZCompanionPC-win-Portable.zip") -ErrorAction SilentlyContinue

# The update package must contain exactly the front-end that was published.
$nupkg = Join-Path $OutDir "KZCompanionPC-$Version-full.nupkg"
if (-not (Test-Path $nupkg)) { throw "missing $nupkg" }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("kzpkg-" + [guid]::NewGuid())
try {
    Copy-Item $nupkg "$tmp.zip"
    Expand-Archive "$tmp.zip" $tmp
    $inPkg = Get-ChildItem $tmp -Recurse -Filter main.dart.js | Select-Object -First 1
    if (-not $inPkg) { throw "installer package has no main.dart.js" }
    $a = (Get-FileHash (Join-Path $PublishDir "wwwroot\main.dart.js") -Algorithm SHA256).Hash
    $b = (Get-FileHash $inPkg.FullName -Algorithm SHA256).Hash
    if ($a -ne $b) { throw "installer front-end differs from the published one" }
    foreach ($w in @("Import from Stream Deck", "Edit profile", "StreamDeck/profiles")) {
        if ([IO.File]::ReadAllText($inPkg.FullName).Contains($w)) { throw "installer front-end contains legacy text '$w'" }
    }
    foreach ($f in "LICENSE", "NOTICE", "THIRD_PARTY_NOTICES.md") {
        if (-not (Get-ChildItem $tmp -Recurse -Filter $f | Select-Object -First 1)) { throw "installer package is missing $f" }
    }
}
finally { Remove-Item $tmp, "$tmp.zip" -Recurse -Force -ErrorAction SilentlyContinue }

Get-ChildItem $OutDir | ForEach-Object { "{0,-48} {1,12:N0} bytes" -f $_.Name, $_.Length }
Write-Host "OK: installer front-end sha256 $a"
