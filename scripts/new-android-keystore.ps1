# Creates the PERMANENT Android release-signing key for KZ Companion and stores it as GitHub Actions secrets.
#
# Run this yourself, once, on your own PC (it asks for a password; nothing secret is printed):
#     .\scripts\new-android-keystore.ps1
#
# What it does
#   1. Checks that gh is logged in and that the signing secrets do not exist yet (so a second identity is never made by accident).
#   2. Creates a PKCS12 keystore OUTSIDE the repository (default: %USERPROFILE%\KZCompanion-signing). Refuses to overwrite one.
#   3. Asks you for a password (hidden). It is used for both the store and the key.
#   4. Sets the four repository secrets with `gh secret set` (values are piped, never printed):
#      ANDROID_KEYSTORE_BASE64, ANDROID_KEY_ALIAS, ANDROID_KEY_PASSWORD, ANDROID_STORE_PASSWORD
#   5. Prints only the public certificate fingerprints.
#
# If the key was created but setting the secrets failed, do NOT delete the key. Re-run with -SecretsOnly:
#     .\scripts\new-android-keystore.ps1 -SecretsOnly
#
# AFTER RUNNING IT you must back up the .jks file AND the password (see docs/android-signing.md).
# If you lose either, you can never publish an update that installs over this release.
param(
    [string]$Repo = "Killzo1602/KZCompanion",
    [string]$Dir = (Join-Path $env:USERPROFILE "KZCompanion-signing"),
    [string]$Alias = "kzcompanion",
    [switch]$SecretsOnly,
    [switch]$ReplaceSecrets
)
$ErrorActionPreference = "Stop"

# Runs a native program and returns @{ Code; Output }. Windows PowerShell 5.1 turns anything a native program
# writes to stderr (keytool prints its progress there) into an ErrorRecord, and with $ErrorActionPreference
# "Stop" that becomes a terminating NativeCommandError even when the program succeeded. So error handling is
# relaxed around the call and success is decided ONLY by the exit code. Output is returned, never echoed:
# callers decide what to show, and no command here receives a password on its command line.
function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments, [string]$InputText = $null)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        if ($null -ne $InputText) { $out = $InputText | & $Exe @Arguments 2>&1 }
        else { $out = & $Exe @Arguments 2>&1 }
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prev }
    $text = ($out | ForEach-Object { if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { "$_" } }) -join "`n"
    return @{ Code = $code; Output = $text }
}

function ConvertTo-Plain($s) { [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)) }

foreach ($tool in "keytool", "gh") {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on PATH (keytool comes with a JDK, gh is the GitHub CLI)." }
}
$root = Split-Path -Parent $PSScriptRoot
if ($Dir.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Choose a folder outside the repository: $Dir" }
$jks = Join-Path $Dir "kzcompanion-release.jks"

# 1. GitHub access and existing secrets (names only, values are never readable).
$auth = Invoke-Native gh @("auth", "status")
if ($auth.Code -ne 0) { throw "gh is not logged in. Run: gh auth login" }
$list = Invoke-Native gh @("secret", "list", "-R", $Repo)
if ($list.Code -ne 0) { throw "Cannot list secrets of $Repo (needs repository admin access): $($list.Output)" }
$existing = @("ANDROID_KEYSTORE_BASE64", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD", "ANDROID_STORE_PASSWORD") | Where-Object { $list.Output -match "(?m)^$_\s" }
if ($existing.Count -gt 0 -and -not $ReplaceSecrets) {
    throw "These secrets already exist on ${Repo}: $($existing -join ', '). Refusing to replace a release identity. Pass -ReplaceSecrets only if you are sure."
}

# 2. The keystore.
if ($SecretsOnly) {
    if (-not (Test-Path $jks)) { throw "-SecretsOnly needs the existing keystore at $jks" }
}
else {
    if (Test-Path $jks) { throw "$jks already exists. Refusing to overwrite a signing key. Use -SecretsOnly to upload the existing one." }
}

$p1 = Read-Host "Keystore password (min 12 characters)" -AsSecureString
$pw = ConvertTo-Plain $p1
if ($SecretsOnly) { $p2 = $p1 } else { $p2 = Read-Host "Repeat the password" -AsSecureString }
if ($pw -ne (ConvertTo-Plain $p2)) { throw "The passwords differ." }
if ($pw.Length -lt 12) { throw "Use at least 12 characters." }

# The password reaches keytool through an environment variable, never the command line or the screen.
$env:KZ_KEYSTORE_PW = $pw
try {
    if (-not $SecretsOnly) {
        New-Item -ItemType Directory -Force $Dir | Out-Null
        Write-Host "Generating the 4096-bit RSA key (can take a few seconds)..."
        $gen = Invoke-Native keytool @("-genkeypair", "-storetype", "PKCS12", "-keystore", $jks, "-alias", $Alias,
            "-keyalg", "RSA", "-keysize", "4096", "-validity", "10000",
            "-storepass:env", "KZ_KEYSTORE_PW", "-keypass:env", "KZ_KEYSTORE_PW",
            "-dname", "CN=KZ Companion, O=KZ Dev Studio")
        if ($gen.Code -ne 0) {
            # A failed run can leave a partial file that this run created (we refused to start if one existed).
            Remove-Item $jks -Force -ErrorAction SilentlyContinue
            throw "keytool failed with exit code $($gen.Code): $($gen.Output)"
        }
        if (-not (Test-Path $jks) -or (Get-Item $jks).Length -lt 1000) { throw "keytool reported success but $jks is missing or empty." }
    }

    # Prove the keystore opens with this password and holds the alias, before anything goes to GitHub.
    $chk = Invoke-Native keytool @("-list", "-v", "-keystore", $jks, "-alias", $Alias, "-storepass:env", "KZ_KEYSTORE_PW")
    if ($chk.Code -ne 0) { throw "The keystore does not open with that password/alias: $($chk.Output)" }

    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($jks))
    foreach ($pair in @(@("ANDROID_KEYSTORE_BASE64", $b64), @("ANDROID_KEY_ALIAS", $Alias), @("ANDROID_KEY_PASSWORD", $pw), @("ANDROID_STORE_PASSWORD", $pw))) {
        $r = Invoke-Native gh @("secret", "set", $pair[0], "-R", $Repo) -InputText $pair[1]
        if ($r.Code -ne 0) {
            throw "Could not set $($pair[0]) (the keystore at $jks is kept; fix gh access and re-run with -SecretsOnly -ReplaceSecrets): $($r.Output)"
        }
        Write-Host "  set $($pair[0])"
    }

    Write-Host ""
    Write-Host "Keystore: $jks"
    Write-Host "Certificate fingerprints (public, safe to share):"
    $chk.Output -split "`n" | Where-Object { $_ -match "SHA1:|SHA256:" } | ForEach-Object { Write-Host "  $($_.Trim())" }
}
finally {
    Remove-Item Env:KZ_KEYSTORE_PW -ErrorAction SilentlyContinue
    $pw = $null; $b64 = $null
}
Write-Host ""
Write-Host "Secrets set on $Repo. NOW BACK UP $jks AND the password (docs/android-signing.md). Do not commit either."
