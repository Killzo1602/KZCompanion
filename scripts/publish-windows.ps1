# Clean Windows x64 publish of KZ Companion: fresh web build, fresh publish folder, verified.
#
# Usage:  .\scripts\publish-windows.ps1 [-Out dist\KZCompanion-win-x64-<commit>] [-Flutter D:\Tools\flutter\bin\flutter.bat]
#
# What it guarantees (the configurator regression came from skipping these):
#   1. The web front-end is rebuilt from app/lib (app\build\web is deleted first).
#   2. DroidDeck\wwwroot (git-ignored, so it silently goes stale) is replaced, not merged.
#   3. The publish goes into a NEW folder and never overwrites a running executable.
#   4. The published wwwroot is byte-identical to the fresh build and has no legacy deck UI.
param(
    [string]$Out = "",
    [string]$Flutter = "flutter",
    [string]$Version = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root "app"
$wwwroot = Join-Path $root "DroidDeck\wwwroot"
$sha = (git -C $root rev-parse --short HEAD).Trim()
$dirty = (git -C $root status --porcelain) -ne $null -and (git -C $root status --porcelain).Length -gt 0
if (-not $Out) { $Out = Join-Path $root ("dist\KZCompanion-win-x64-" + $sha + $(if ($dirty) { "-wip" } else { "" })) }
elseif (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path $root $Out }

# Words that only the removed DroidDeck button deck had, and the labels the configurator must have.
$legacy = @("Import from Stream Deck", "Automations", "Profiles", "StreamDeck/profiles", "Edit profile")
$required = @("Applications", "Integrations", "Appearance")

function Assert-WebClean($dir, $label) {
    $js = Join-Path $dir "main.dart.js"
    if (-not (Test-Path $js)) { throw "$label has no main.dart.js" }
    $text = [IO.File]::ReadAllText($js)
    foreach ($w in $legacy) { if ($text.Contains($w)) { throw "$label contains legacy deck text '$w'" } }
    foreach ($w in $required) { if (-not $text.Contains($w)) { throw "$label is missing '$w' (stale or wrong front-end)" } }
}

# 3. never overwrite a running executable
$full = [IO.Path]::GetFullPath($Out)
$running = Get-Process KZCompanion -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($full, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "KZ Companion is running from $full (pid $($running.Id -join ',')). Quit it from the tray, or pass a different -Out." }

# 1. clean web build
Write-Host "== Web build"
$buildWeb = Join-Path $app "build\web"
if (Test-Path $buildWeb) { Remove-Item $buildWeb -Recurse -Force }
Push-Location $app
& $Flutter build web --release
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { throw "flutter build web failed ($code)" }
Assert-WebClean $buildWeb "fresh web build"

# 2. replace (not merge) the backend's wwwroot
Write-Host "== Replace DroidDeck\wwwroot"
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
Copy-Item $buildWeb $wwwroot -Recurse
Assert-WebClean $wwwroot "DroidDeck\wwwroot"

# 3. publish into a new folder
Write-Host "== Publish -> $full"
if (Test-Path $full) { Remove-Item $full -Recurse -Force }
$verArg = @(); if ($Version) { $verArg = @("-p:Version=$Version") }
dotnet publish (Join-Path $root "DroidDeck\DroidDeck.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false @verArg -o $full --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Licenses travel with the binaries.
foreach ($f in "LICENSE", "NOTICE", "THIRD_PARTY_NOTICES.md") {
    if (-not (Test-Path (Join-Path $root $f))) { throw "$f is missing from the repository root" }
    Copy-Item (Join-Path $root $f) (Join-Path $full $f)
}

# 4. verify the published front-end is the fresh one
$a = (Get-FileHash (Join-Path $buildWeb "main.dart.js") -Algorithm SHA256).Hash
$b = (Get-FileHash (Join-Path $full "wwwroot\main.dart.js") -Algorithm SHA256).Hash
if ($a -ne $b) { throw "published wwwroot differs from the fresh web build" }
Assert-WebClean (Join-Path $full "wwwroot") "published wwwroot"
"commit=$sha$(if ($dirty) { ' (uncommitted changes)' })`nbuilt=$(Get-Date -Format s)`nmain.dart.js sha256=$a" | Set-Content (Join-Path $full "BUILD.txt")

Write-Host ""
Write-Host "OK: $full\KZCompanion.exe"
Write-Host "main.dart.js sha256 $a"
Write-Host "Launch: quit any running KZ Companion from the tray first (a second launch only wakes the old one), then run KZCompanion.exe from that folder."
