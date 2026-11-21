# KZ Companion v1.0.0: release checklist

Status: **not released.** No tag, GitHub Release or binary has been published. This file records what was
verified on 2026-11-21 (commit `75cea72` plus the release-preparation changes) and what still blocks a release.

## Verification results

| Check | Result |
|---|---|
| Backend tests (`dotnet test`, excluding `RequiresDesktop`) | **Cannot run as-is on the dev PC** (runtime mismatch, below). With `DOTNET_ROLL_FORWARD=Major` they run on .NET 10: 519 passed, 1 failed. The failure was a flaky test (a File Explorer window titled `D:\` tripped the "no file paths in JSON" assertion); the test now ignores window titles. After that fix the re-run on .NET 10 gave **520 passed, 0 failed**. Tests were **not** run on a .NET 8.0.34 runtime. |
| `RequiresDesktop` tests | Not run (need an interactive desktop; excluded in CI). |
| `flutter analyze --no-fatal-infos` | 0 errors, 0 warnings, 10 info-level hints (style only). |
| `flutter test` | 66 passed, 2 skipped, 0 failed. |
| Windows publish (`scripts/publish-windows.ps1`) | **Passed** into a new folder (`dist/KZCompanion-win-x64-rc-verify`, the running app was not touched). Fresh web build, `main.dart.js` sha256 `335BC8F4...18D288`, identical in the published `wwwroot`, no legacy text. The script fails if the published `wwwroot` differs from the fresh web build or contains legacy deck text. |
| Android release APK | `flutter build apk --release` **passed** (66.5 MB). It is signed with the Android **debug** key (no release keystore configured), as is the existing `dist/KZCompanion.apk`. |
| Credentials in the repository | None found: no keystores, `.env`, tokens or private keys tracked or in history. `key.properties` and `*.jks` are git-ignored. Integration secrets are written at runtime to `%LocalAppData%\KZCompanion`, not the repo. |
| License / NOTICE | **Inconsistent: `app/LICENSE` is GPL-3.0 (see Blocker 0).** Root `LICENSE` is the unmodified Apache 2.0 text. `NOTICE` names KZ Dev Studio, the upstream DroidDeck copyright and the changes, and LibreHardwareMonitorLib (MPL-2.0). Other third-party packages (NAudio, MQTTnet, NLog, QRCoder, Velopack, WebView2, Flutter packages) are not listed in `NOTICE`; they are permissively licensed but a third-party notices file is recommended. |
| Update behavior | `AppUpdater` checks `https://github.com/Killzo1602/KZCompanion` Releases every 6 h, only on the Velopack-installed copy (not the zip). Unverified end to end: needs two published versions. |
| Legacy UI assets | The publish script rejects the strings `Import from Stream Deck`, `Automations`, `Profiles`, `StreamDeck/profiles`, `Edit profile` in `main.dart.js`. The inherited `website/` still contains the old deck (banner added, publishing disabled). |

### The .NET test runtime mismatch (root cause)

`dotnet test` aborts with *"You must install or update .NET... Framework: Microsoft.NETCore.App 8.0.34"*.
The dev PC has `Microsoft.AspNetCore.App 8.0.34` but only `Microsoft.NETCore.App` / `WindowsDesktop.App 8.0.19`.
The ASP.NET Core 8.0.34 shared framework declares a dependency on `Microsoft.NETCore.App 8.0.34`
(`shared\Microsoft.AspNetCore.App\8.0.34\Microsoft.AspNetCore.App.runtimeconfig.json`), so the test host
cannot start. The repository is not at fault: CI uses `actions/setup-dotnet` with `8.0.x`, which installs matching runtimes.

Fix on the machine: install the current .NET 8 runtimes (**.NET Runtime** and **.NET Desktop Runtime** 8.0.34 or newer).
Workaround: `DOTNET_ROLL_FORWARD=Major dotnet test ...` (runs on .NET 10, so it is not a perfect substitute).

## Blockers

0. **License inconsistency in `app/`.** The repository root is Apache 2.0, but `app/LICENSE` is the full **GNU GPL v3** text.
   It arrived with upstream's import of the Flutter app (`03f50f4`, "Add 'app/' from commit fbc7d1d...") and no source file
   mentions GPL. Which license governs `app/` is unclear. This must be resolved (ask the upstream author, or take legal advice)
   before publishing the APK. If `app/` is GPL-3.0, an Apache-2.0 label on the combined release is wrong, and the NOTICE must say so.

1. **Android release signing.** No release keystore exists and no `ANDROID_*` secrets are set on the repository, so the
   Release workflow falls back to the **debug key**. A debug-signed APK cannot be updated later by a differently signed one.
   Create a keystore, back it up safely, and add `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`,
   `ANDROID_KEY_PASSWORD`, `ANDROID_STORE_PASSWORD` as repository secrets before the first public APK.
2. **GitHub Issues is disabled** on the repository (`has_issues: false`). The README, templates and CONTRIBUTING
   all point to Issues. Enable it in Settings, and enable private vulnerability reporting for `SECURITY.md`.
3. **Real screenshots are missing.** See `docs/screenshots/README.md`. The README says so honestly; replace that section once captured.
4. **No test on .NET 8.0.34 and no CI run on the fork.** Install the matching runtime locally or run the CI workflow
   (Actions > CI > Run workflow) and confirm it is green.
5. **Live verification of integrations.** Discord, Spotify and OBS setup have not been exercised against live accounts. The README labels
   them accordingly; either test them or keep the wording.

## Should fix (not strictly blocking)

- Windows installer and exe are **not code-signed**; SmartScreen will warn. The README says so.
- The Android welcome and tray texts were renamed in English and Spanish; **Portuguese and Hindi still show "DroidDeck"**
  because their text comes from the Portuguese source strings in code.
- Update `website/` (inherited DroidDeck docs) or delete it before enabling GitHub Pages.
- Log file is still `%LocalAppData%\DroidDeck.log`, and the project/solution are named `DroidDeck` (assembly is `KZCompanion`).
- Add a third-party notices file.
- The Tuya integration uses another product's public registration; it is on hold and not exposed.

## Publishing steps (when blockers are cleared)

1. Confirm `main` is green in CI and the working tree is clean.
2. Confirm the repository secrets above exist.
3. Decide what to do with the hand-written "Unreleased: 1.0.0" section of `CHANGELOG.md`: the release tooling
   prepends its own generated notes above it, so either delete that section first or accept the duplicate.
4. Run the **Release** workflow manually (Actions > Release > Run workflow). With no existing tags, `semantic-release` computes **1.0.0**.
   It runs tests, builds `KZCompanion-Setup.exe`, `KZCompanion-win-x64.zip`, `KZCompanion.apk` plus the Velopack update files
   (`*.nupkg`, `releases.win.json`, `assets.win.json`, `RELEASES`), and creates the tag and the Release.
5. Open the Release page and verify the asset names and that the notes start with the install header from `.github/release-header.md`.
6. Install the published `KZCompanion-Setup.exe` on a clean Windows PC and the APK on a phone; pair with QR; check Dashboard, Mixer, Apps and Running Apps.
7. Fill in the Screenshots section and re-check the README links.
8. For the first update test, publish a later 1.0.x and confirm the installed app updates itself.

Do not run `git tag` or create the Release by hand; the workflow does both and updates `CHANGELOG.md`.
Do not overwrite a running copy of the app: publish into a new folder (the script refuses otherwise).
