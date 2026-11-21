# KZ Companion v1.0.0: release checklist

Status: **prepared, NOT published.** No tag, GitHub Release or binary exists. Last verified 2026-11-21.

## Blockers (owner action needed)

1. **Android APK: unresolved license question.** The repository root is Apache-2.0, but `app/LICENSE` is the full GPL-3.0 text, and the
   APK bundles Google ML Kit (proprietary). Under Apache-2.0 that is fine; under GPL-3.0 it is a real conflict. The upstream author
   has not confirmed which license covers `app/`. Until they do, the APK must not be published. Evidence and options:
   [license-audit.md](license-audit.md). Suggested next step: post this on <https://github.com/ggfto/DroidDeck/issues> (not done by me, it is
   your decision to contact them):

   > Hi, I'm building a fork (KZ Companion). Your root `LICENSE` and README say Apache-2.0 (added 2026-10-01), but `app/LICENSE`
   > is the full GPL-3.0 text from the original Flutter app repository. Could you confirm which license applies to `app/`? If it is
   > Apache-2.0, could you replace `app/LICENSE` (or say so in a comment)? Thanks!

2. **Android release key does not exist yet.** No `ANDROID_*` secrets are set, and the release workflow refuses to run without them.
   Run `.\scripts\new-android-keystore.ps1` on your PC, then **back up the `.jks` and its password** (see [android-signing.md](android-signing.md)).
   I deliberately did not create this permanent identity for you.

When both are done, run **Actions > Release > Run workflow** with version `1.0.0`. (If you prefer to publish the Windows installer first and
the APK later, the workflow needs a small change to skip the Android job; ask for it. It is not implemented.)

## Verification results (2026-11-21)

| Check | Result |
|---|---|
| Flutter analyzer | 0 errors, 0 warnings, 10 info-level style hints |
| Flutter tests | 66 passed, 2 skipped, 0 failed |
| Backend tests | 520 passed, 0 failed, **on .NET 10 via `DOTNET_ROLL_FORWARD=Major`**. Not run on .NET 8.0.34 (see below). `RequiresDesktop` tests are excluded (they need an interactive desktop). |
| Windows publish (`scripts/publish-windows.ps1 -Version 1.0.0`) | Passed. Fresh web build, `main.dart.js` sha256 `335BC8F4...18D288`, identical in the published `wwwroot`, no legacy deck text. `LICENSE`, `NOTICE`, `THIRD_PARTY_NOTICES.md` copied in. |
| Installer (`scripts/build-installer.ps1`) | Built: `KZCompanion-Setup-1.0.0.exe` (115.6 MB, x64). The update package contains the same front-end hash and the license files. Found and fixed: it was being packed without `--runtime win-x64` (Velopack defaulted to x86). |
| Install / uninstall | Passed on this PC, silent install into a scratch folder: Add/Remove Programs entry "KZ Companion", publisher "KZ Dev Studio", version 1.0.0; Desktop and Start-menu shortcuts with the exe icon; **no UAC prompt** (per-user install; the exe manifest is `asInvoker`). Uninstall removed the folder, registry key and shortcuts. The running copy of the app and `%LocalAppData%\KZCompanion` were not touched. |
| Data preservation on update | By design only: app data lives in `%LocalAppData%\KZCompanion`, outside the Velopack install folder, which updates replace. **No real update (1.0.0 to 1.0.1) was run.** |
| Tray behavior of the installed copy | **Not verified.** Starting a second copy would collide with the app running on this PC (single-instance). Needs a clean PC or VM. |
| Android APK (arm64) | Built with `flutter build apk --release --target-platform android-arm64`. **Signed only with a throwaway test key** to prove the pipeline: `scripts/verify-apk.sh` passes (valid v2 signature, not the debug key, package `dev.kzstudio.kzcompanion`, versionName `1.0.0`, only `arm64-v8a`, 24.4 MB). It correctly **fails** on the old debug-signed APK, on a wrong version, and on a multi-ABI APK. Found and fixed: dependencies added armeabi-v7a and x86_64 libraries. No real release APK exists yet. |
| Release workflow | Written and YAML-validated, but **never executed** (it needs the signing secrets and the license decision). It runs the same scripts used above. |
| Secret scan | 514 files scanned with common token and key patterns: only format strings and existing test fixtures matched. No keystores, `.env` or `key.properties` tracked; none in history. |
| Dependency licenses | 142 Flutter and 18 NuGet packages inventoried into `THIRD_PARTY_NOTICES.md`: no GPL/LGPL dependency. MPL-2.0 libraries unmodified. ML Kit is proprietary (see blocker 1). |
| GitHub repository | Issues **enabled** (was disabled), private vulnerability reporting **enabled**, secret scanning and push protection already on. |
| Physical devices | **None tested.** No phone was used, and no real pairing or integration was exercised in this release work. |

### The .NET test runtime mismatch (unchanged)

`dotnet test` aborts on this PC with *"You must install or update .NET... Microsoft.NETCore.App 8.0.34"*: the machine has ASP.NET Core 8.0.34 but
only .NET runtime 8.0.19, and ASP.NET's own runtime config requires the matching 8.0.34 runtime. Fix: install the current .NET 8 runtimes
(.NET Runtime and .NET Desktop Runtime). GitHub's `windows-latest` with `setup-dotnet 8.0.x` is not affected, and the Release workflow runs the tests there.

## Known limitations for the release notes

Windows installer unsigned (SmartScreen warning); plain HTTP on the LAN; ARM64 Android only; integrations unverified; elevated apps locked;
the Android license question above. All are in `docs/release-notes/v1.0.0.md`.

## Release steps

1. Resolve blockers 1 and 2.
2. Confirm `main` is green: Actions > CI > Run workflow (CI has never run on this fork).
3. Update `docs/license-audit.md`, `NOTICE` and the README license table with the upstream author's answer.
4. Actions > Release > Run workflow, version `1.0.0`. It checks the secrets, tag and notes, tests, builds both artifacts, verifies the front-end
   and the APK signature, writes `SHA256SUMS.txt`, creates a **draft** release, pushes the annotated tag `v1.0.0` and then publishes.
5. Verify on the Releases page: `KZCompanion-Setup-1.0.0.exe`, `KZCompanion-Android-arm64-v1.0.0.apk`, `SHA256SUMS.txt`, notices, the update
   files, and the two source archives GitHub adds. Download both binaries and compare their SHA-256 with `SHA256SUMS.txt`.
6. Install the published installer on a clean Windows PC and the APK on a phone; pair with QR; check Dashboard, Mixer, Apps and Running Apps.
7. To test auto-update, publish a later 1.0.x and confirm the installed app updates itself and keeps its data.

Never create the tag or release by hand, and never overwrite a running copy of the app.
