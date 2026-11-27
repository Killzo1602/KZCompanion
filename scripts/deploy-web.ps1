$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'app'
$wwwroot = Join-Path $root 'DroidDeck\wwwroot'

Write-Host "Building Flutter web in $app ..."
Push-Location $app
flutter build web --release
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { Write-Error "flutter build web failed (exit $code)"; exit 1 }

robocopy (Join-Path $app 'build\web') $wwwroot /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -lt 8) {
    Write-Host "Web deployed to $wwwroot (robocopy exit $LASTEXITCODE)"
    exit 0
} else {
    Write-Error "robocopy failed (exit $LASTEXITCODE)"
    exit 1
}
