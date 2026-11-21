# Creates the PERMANENT Android release-signing key for KZ Companion and stores it as GitHub Actions secrets.
#
# Run this yourself, once, on your own PC (it asks for a password; nothing secret is printed):
#     .\scripts\new-android-keystore.ps1
#
# What it does
#   1. Creates a PKCS12 keystore OUTSIDE the repository (default: %USERPROFILE%\KZCompanion-signing).
#   2. Asks you for a password (hidden). It is used for both the store and the key.
#   3. Sets the four repository secrets with `gh secret set` (the values are piped, never printed):
#      ANDROID_KEYSTORE_BASE64, ANDROID_KEY_ALIAS, ANDROID_KEY_PASSWORD, ANDROID_STORE_PASSWORD
#   4. Prints only the certificate SHA-256 fingerprint.
#
# AFTER RUNNING IT you must back up the .jks file AND the password (see docs/android-signing.md).
# If you lose either, you can never publish an update that installs over this release.
param(
    [string]$Repo = "Killzo1602/KZCompanion",
    [string]$Dir = (Join-Path $env:USERPROFILE "KZCompanion-signing"),
    [string]$Alias = "kzcompanion"
)
$ErrorActionPreference = "Stop"

foreach ($tool in "keytool", "gh") {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on PATH (keytool comes with a JDK, gh is the GitHub CLI)." }
}
$root = Split-Path -Parent $PSScriptRoot
if ($Dir.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Choose a folder outside the repository: $Dir" }
$jks = Join-Path $Dir "kzcompanion-release.jks"
if (Test-Path $jks) { throw "$jks already exists. Refusing to overwrite a signing key. Move it away deliberately if you really want a new identity." }

$p1 = Read-Host "Choose a keystore password (min 12 characters)" -AsSecureString
$p2 = Read-Host "Repeat the password" -AsSecureString
function ConvertTo-Plain($s) { [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)) }
$pw = ConvertTo-Plain $p1
if ($pw -ne (ConvertTo-Plain $p2)) { throw "The passwords differ." }
if ($pw.Length -lt 12) { throw "Use at least 12 characters." }

New-Item -ItemType Directory -Force $Dir | Out-Null
# The password reaches keytool through an environment variable, not the command line or the screen.
$env:KZ_KEYSTORE_PW = $pw
try {
    & keytool -genkeypair -v -storetype PKCS12 -keystore $jks -alias $Alias -keyalg RSA -keysize 4096 -validity 10000 `
        -storepass:env KZ_KEYSTORE_PW -keypass:env KZ_KEYSTORE_PW `
        -dname "CN=KZ Companion, O=KZ Dev Studio" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "keytool failed" }

    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($jks))
    $b64 | & gh secret set ANDROID_KEYSTORE_BASE64 -R $Repo
    $Alias | & gh secret set ANDROID_KEY_ALIAS -R $Repo
    $pw | & gh secret set ANDROID_KEY_PASSWORD -R $Repo
    $pw | & gh secret set ANDROID_STORE_PASSWORD -R $Repo

    Write-Host ""
    Write-Host "Keystore: $jks"
    Write-Host "Certificate fingerprints (public, safe to share):"
    & keytool -list -v -keystore $jks -alias $Alias -storepass:env KZ_KEYSTORE_PW | Select-String "SHA1:|SHA256:"
}
finally {
    Remove-Item Env:KZ_KEYSTORE_PW -ErrorAction SilentlyContinue
    $pw = $null
}
Write-Host ""
Write-Host "Secrets set on $Repo. NOW BACK UP $jks AND the password (docs/android-signing.md). Do not commit either."
