# Android release signing

Android updates only install over an app signed with the **same key**. The release key is therefore permanent:
if it is lost, no future update can install over v1.0.0 and everyone has to uninstall and reinstall.

## One-time setup (owner)

On your own PC (needs a JDK for `keytool` and the GitHub CLI logged in as the repository owner):

```powershell
.\scripts\new-android-keystore.ps1
```

It creates `%USERPROFILE%\KZCompanion-signing\kzcompanion-release.jks` (outside the repository), asks for a password
(hidden), and sets these repository secrets without printing them: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`,
`ANDROID_KEY_PASSWORD`, `ANDROID_STORE_PASSWORD`. It prints only the public certificate fingerprints.
It refuses to overwrite an existing keystore, and refuses to run if the signing secrets already exist (so a second identity is never created by accident).
If the key was created but uploading the secrets failed, keep the key and run `.\scripts
ew-android-keystore.ps1 -SecretsOnly -ReplaceSecrets` to upload it again.

Manual alternative: create the keystore with `keytool -genkeypair -v -storetype PKCS12 -keystore kzcompanion-release.jks -alias kzcompanion -keyalg RSA -keysize 4096 -validity 10000`,
then add the four secrets under Settings, Secrets and variables, Actions.

## Back it up (do this immediately)

1. Copy `kzcompanion-release.jks` to at least two separate places you control: for example an encrypted USB drive kept
   offline and a password-manager attachment or encrypted cloud vault.
2. Store the password in your password manager, separate from the file.
3. Record the SHA-256 fingerprint the script printed (it is public) so you can check a restored copy:
   `keytool -list -v -keystore kzcompanion-release.jks -alias kzcompanion`.
4. Never commit the `.jks` file, `key.properties` or the password. Both are git-ignored, and the repository has secret scanning enabled.

GitHub secrets cannot be read back, so the GitHub copy is **not** a backup.

## How builds use it

- The Release workflow fails in preflight if any `ANDROID_*` secret is missing.
- It restores the keystore into `app/android/key.properties` and `app/android/app/release.jks` on the runner, and sets
  `KZ_REQUIRE_RELEASE_SIGNING=true`, which makes Gradle fail instead of falling back to the debug key.
- `scripts/verify-apk.sh` then fails the build unless the APK has a valid v2 signature **not** from the Android debug key,
  package `dev.kzstudio.kzcompanion`, the expected `versionName`, and only `arm64-v8a` native code.
- The keystore and `key.properties` are deleted at the end of the job.

Verify any APK yourself: `bash scripts/verify-apk.sh KZCompanion-Android-arm64-v1.0.0.apk 1.0.0`.

## Upgrade limitation for debug-signed installs

Builds made from source without a keystore are signed with the Android **debug** key. Android will refuse to install the
release APK over such a build ("App not installed" / signature mismatch). Uninstall the debug build first; its app data
(including the saved PC pairing) is lost, so pair again afterwards. The same applies the other way round.
