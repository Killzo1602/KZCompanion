# License compliance audit (v1.0.0 preparation)

Baseline: `4453cbc` · Date: 2026-11-21 · Scope: audit only. **No license file was changed, deleted or replaced.**

This is an engineering review of the repository history and dependency metadata. It is **not legal advice**.
Evidence is labelled: **[git]** read from commit history, **[file]** read from files in the repo or dependency
packages, **[infer]** my inference from the evidence, **[unknown]** cannot be determined from what is available.

## 1. Summary

1. The repository root `LICENSE` is Apache-2.0. `app/LICENSE` is the full GPL-3.0 text. The two statements conflict. **[file]**
2. Both come from the same upstream author. The GPL file is the one created at repository creation in 2021 and 2023.
   The Apache-2.0 file was added by that author on 2026-10-01 and the upstream README says "Apache License 2.0".
   The upstream author never removed or updated `app/LICENSE`. **[git]**
3. Most likely reading **[infer]**: the upstream author (effectively the sole copyright holder of the code that existed
   before October 2026) relicensed the project from GPL-3.0 to Apache-2.0 on 2026-10-01 and left a stale GPL file in `app/`.
   That is plausible, but it is **not confirmed by the author**, so it cannot be treated as settled.
4. No source file carries a GPL or Apache header, and no GPL-licensed dependency is shipped in either binary. **[file]**
5. The current statements in this repository are **not fully accurate**: the README and `NOTICE` say the project is
   Apache-2.0 and do not mention the GPL file or the unresolved question. (The README already warns about the `app/LICENSE`
   inconsistency; `NOTICE` does not.)
6. Recommended smallest fix: ask the upstream author to confirm in writing (a GitHub issue or commit) that `app/` is
   Apache-2.0 and to replace their `app/LICENSE`; then mirror that. Until then, do not publish the APK as plain "Apache-2.0".
   See section 8.

## 2. Original license of DroidDeck and where `app/LICENSE` came from

| Date | Commit | Event |
|---|---|---|
| 2021-03-31 | `5e2ab21` "Initial commit" (Gabriel Guimaraes) | First root commit of the Windows/backend lineage (later `RaspDeck`/`AnyDeck`). Contains only `LICENSE` (GPL-3.0) and `.gitattributes`: the file GitHub offers when creating a repository. **[git]** |
| 2022-04-26 | `a05b340` "mudança no front end" | That `LICENSE` is **deleted** from the backend lineage. Reason not stated. **[git]** |
| 2023-03-21 | `d635753` "Initial commit" (Gabriel Freitas) | First root commit of the Flutter app lineage. Adds `LICENSE` (GPL-3.0), `.gitignore`, `README.md`. **[git]** |
| 2026-06-03 | `03f50f4` "Add 'app/' from commit fbc7d1d" | The author merges the app repository into the monorepo with `git subtree`, as `app/`. The GPL file becomes `app/LICENSE`. **[git]** |
| 2026-10-01 | `6251c3c` "docs: site ... licenca Apache 2.0" | The author adds the root `LICENSE` (Apache-2.0) and a README license section "Apache License 2.0". **[git]** |
| 2026-11-04 | `8cd33d7` (fork point) | Upstream is at `1.14.0`. Upstream `HEAD` still has the root Apache `LICENSE` and the GPL `app/LICENSE`. **[git]** |
| 2026-11-21 | `47d4950` (KZ Dev Studio) | KZ Companion rebrand adds `NOTICE`. `app/LICENSE` left untouched. **[git]** |

Between 2022-04-26 and 2026-10-01 the backend had **no license file at all** **[git]**. GitHub reports the upstream
repository's license as Apache-2.0 **[file: GitHub API]**. The author's own contributor guide says nothing about licenses.

Conclusion: the GPL-3.0 text was the author's own choice at repository creation, for both halves of the project, and
the author then moved the monorepo to Apache-2.0 without touching `app/LICENSE`.

## 3. Who holds the copyright

Commit authors in the whole history **[git]**:

| Author (identity) | Commits | Notes |
|---|---|---|
| Gabriel Freitas / Guimaraes (several names, one email `ggfto@...`) | about 190+ | The upstream author. Three further commits, dated 2022-05-06, are under a different, corporate email address. |
| `semantic-release-bot` | 18 | Release commits only |
| Killzo1602 (KZ Dev Studio) | 16 | This fork's changes, all after the fork point |
| "Aaqibhafeez Khan" | 1 | Hindi translation, 2026-10-23 (after the Apache relicense) |
| "Doomguy" | 1 | Spanish landing page in `website/`, 2026-10-18 (after the Apache relicense) |

**Why this matters [infer]:** all code that existed under GPL-3.0 (before 2026-10-01) was written by one person. A sole
copyright holder may license their own work under different terms in later versions. The only two outside contributions
came **after** the Apache switch, so they were made to an Apache-licensed project.

**Unresolved [unknown]:** whether the three 2022 commits under a corporate email address give an employer any claim.
Nothing in the repository says. Very likely irrelevant, but only the author can say.

## 4. What is covered by which license

### 4.1 First-party code (upstream and KZ)

| Component | Declared license(s) | Evidence |
|---|---|---|
| `DroidDeck/` (Windows app), `tests/`, `scripts/`, `website/`, `docs/` | Apache-2.0 (root `LICENSE`, README). No GPL file applies, except that the 2021 backend repository was created with GPL-3.0 and the file was removed in 2022. | **[git]** |
| `app/` (Flutter: Android app and web configurator) | **Conflicting**: GPL-3.0 (`app/LICENSE`) and Apache-2.0 (root `LICENSE`, applies to the repository). | **[file]** |
| KZ Dev Studio additions (e.g. `app/lib/src/kz/`, KZ parts of the Windows app) | Intended Apache-2.0 per `NOTICE`. Their combination with `app/` follows whatever governs `app/`. | **[file]** |

No source file contains a license header, copyright line, SPDX tag or "GNU"/"GPL" text. A search for `copyright`,
`licensed under`, `SPDX` and `(c)` in `*.cs`, `*.dart`, `*.kt`, `*.kts`, `*.html` and `*.js` outside build output found
only false positives (variables named `c`). **[file]** So the GPL applies, if at all, only through `app/LICENSE`, and
the version (GPL-3.0-only or later) is not stated per file.

### 4.2 Third-party dependencies shipped in the binaries

**Android APK (Flutter, 146 locked packages, read from the local pub cache)** **[file]**

| License | Packages | Notes |
|---|---|---|
| BSD (3-clause and similar) | 117 | Includes the Flutter engine and most Google/Flutter-team packages |
| MIT | 13 | |
| Apache-2.0 | 10 | |
| MPL-2.0 | `dbus`, `nm` | Linux-only plug-ins. Not part of the Android or web build. (A naive text search flags them as "GNU" because the MPL text mentions the GNU licenses; they are not GPL.) |
| SDK | `flutter`, `flutter_test`, `flutter_web_plugins`, `sky_engine` | BSD-3-Clause |
| **Google ML Kit barcode scanning (bundled)** | `com.google.mlkit:barcode-scanning:17.3.0`, via `mobile_scanner` | **Proprietary / Google terms, not open source.** The release APK bundles its model files (`assets/mlkit_barcode_models/*.tflite`, confirmed in the built APK). See section 6. |
| AndroidX CameraX, other Android libraries | Apache-2.0 | Not individually audited |

No GPL or LGPL Flutter dependency is present. **[file]**

**Windows app (NuGet, read from nuspec files)** **[file]**

| License | Packages |
|---|---|
| MIT | MQTTnet, QRCoder, Velopack, Swashbuckle.AspNetCore, System.Management, System.Diagnostics.PerformanceCounter, Newtonsoft.Json |
| BSD | NLog (3-clause), NLog.Extensions.Logging (2-clause) |
| MPL-2.0 | LibreHardwareMonitorLib, DiskInfoToolkit, RAMSPDToolkit-NDD, BlackSharp.Core |
| Apache-2.0 | HidSharp (license file, header "Copyright 2010-2025 James F. Bellinger") |
| Separate Microsoft / NAudio license files | Microsoft.Web.WebView2 (Microsoft license), NAudio (own license file, MIT-style) |
| **Not declared** | LocalStorage 2.1.0 (no license element in package metadata) and Mono.Posix.NETStandard (license URL only). Need a manual check. |

No GPL or LGPL .NET dependency is present. **[file]** The PawnIO kernel driver for CPU sensors is **not bundled**: the
app offers to install it through `winget` after the user confirms (`frmPrincipal.cs`), so its license does not attach
to the KZ Companion installer. The `adb` tool is likewise downloaded by the user on request, not shipped.

## 5. Does the Android app contain GPL-covered code?

**Answer: only if `app/LICENSE` is taken at face value.** The Android app is built from `app/`. If the GPL-3.0 file is
authoritative, then the app's first-party code is GPL-3.0 code. No third-party GPL code is inside it. The question is
purely the license of the upstream author's own code in `app/`, which is not settled in the repository.

## 6. Are the current licensing statements accurate?

| Statement | Assessment |
|---|---|
| Root `LICENSE`: Apache-2.0, unmodified text | Accurate as a file. Apache-2.0 appendix has no copyright line to fill in; upstream also left it blank. |
| README: "© 2026 KZ Dev Studio, licensed under Apache-2.0" and "fork of DroidDeck, also Apache 2.0" | **Overstated.** True for upstream's *declared* project license, but upstream's own `app/LICENSE` says GPL-3.0, so "also Apache 2.0" is not established for `app/`. The README does flag the inconsistency, but a visitor reading the first lines is told it is plain Apache-2.0. |
| `NOTICE`: modified fork of DroidDeck, "Copyright Gabriel Freitas, licensed under the Apache License, Version 2.0" | **Incomplete.** Correct attribution, but silent on `app/LICENSE`. Its "Changes" line is also shorter than the real set of changes. It does name LibreHardwareMonitorLib (MPL-2.0). It does not name the other MPL-2.0 and third-party components. |
| `app/LICENSE` kept intact | Correct: it must not be removed by a downstream. It is currently the only upstream statement on `app/`. |
| No third-party notices file | **Gap.** Apache-2.0 §4(d) and the MPL-2.0 require notices to travel with distributions. The pub and NuGet licenses (BSD, MIT, Apache, MPL) also require copyright notices in distributions. Flutter shows packages' licenses in the app's "licenses" page automatically, but the Windows app has no equivalent. |
| Bundled ML Kit | If `app/` is GPL-3.0, bundling the proprietary ML Kit libraries and model in the APK is a **conflict** with GPL's rules about proprietary components (the system-library exception does not clearly cover a library bundled into the app). Under Apache-2.0 there is no such issue. **[infer, legal question]** |

## 7. Implications for distribution

**Windows app (installer and zip).** Built from `DroidDeck/` (Apache-2.0 per upstream declaration; its only GPL-3.0 file
was deleted in 2022) plus MIT/BSD/MPL/Apache dependencies. Obligations: ship `LICENSE` and `NOTICE`, keep upstream
copyright notices, state modifications, provide third-party notices, and for the MPL-2.0 libraries make their source
available (pointing to the unmodified upstream projects is normally enough). No GPL dependency, so **no copyleft
obligation falls on the Windows app** [infer]. The Windows app and the APK are separate programs that talk over the network, so a
GPL on the APK would not by itself extend to the Windows app. [infer, not legal advice]

**Android APK.** Depends on the unresolved question:

- *If `app/` is Apache-2.0* (author confirms): ship `LICENSE`, `NOTICE` and third-party notices; no GPL obligations; ML Kit is fine.
- *If `app/` is GPL-3.0*: the APK, as a combined work, must be distributed under GPL-3.0 with corresponding source and
  the license text. The public repository would satisfy source availability. KZ Dev Studio's own additions to `app/` could not be offered
  under Apache-2.0 alone. The bundled ML Kit models are then a **real compatibility risk**. Switching mobile_scanner to the
  *unbundled* variant (Google Play Services downloads the model; `dev.steenbakker.mobile_scanner.useUnbundled=true`)
  would reduce, but not obviously remove, that risk, and it would stop QR pairing from working on devices without Google Play Services.
- Already-distributed GPL copies stay GPL for their recipients regardless; relicensing only affects later versions.

## 8. Proposed smallest compliant solution (not applied)

Nothing below has been applied. It needs your approval.

**Step 1 (needed, no repo change): get the upstream author's confirmation.**
Open an issue (or PR) on <https://github.com/ggfto/DroidDeck> asking the author to confirm that all of `app/` is licensed
under Apache-2.0 since 2026-10-01, and to replace or remove their `app/LICENSE`. Save the link in this audit. The author is
the only party who can resolve it, and the evidence in section 2 makes "yes, stale file" the likely answer.

**Step 2a: if the author confirms Apache-2.0.** Smallest change:
- keep `app/LICENSE` until upstream replaces it, and add one line to `NOTICE` and to the README explaining that the GPL-3.0 file in `app/` is a stale upstream file, citing the author's confirmation (link);
- add a `THIRD_PARTY_NOTICES.md` (generated from the lists in section 4) and ship it with both binaries;
- finish the `NOTICE` change list.

**Step 2b: if the author does not answer or says `app/` is GPL-3.0.** The compliant fallback is to treat `app/` as GPL-3.0:
- distribute the APK under GPL-3.0, with source (this public repository), say so in README and `NOTICE`, and keep Apache-2.0 for the Windows app;
- resolve the bundled ML Kit question before shipping (see section 7);
- KZ Dev Studio's changes inside `app/` would be GPL-3.0 as part of that combined work.

**Do not**: delete `app/LICENSE`, relabel the whole project as Apache-2.0 without the author's confirmation, or
claim "fully Apache-2.0" on the Releases page until step 1 is answered.

## 9. Unresolved legal questions

1. Is `app/` governed by GPL-3.0 (file in `app/`) or Apache-2.0 (root file and README)? Only the upstream author can confirm. If GPL, is it "3.0 only" or "or later"? (There are no per-file notices.)
2. Was the 2022 deletion of the GPL file from the backend lineage a deliberate relicense? Which license, if any, covered backend code from 2022 to October 2026?
3. If `app/` is GPL-3.0, is bundling Google ML Kit in the APK compatible? (Proprietary terms and a bundled model; counsel needed.)
4. Do the three 2022 commits under a corporate email address carry any employer claim? Almost certainly not, but it is unverified.
5. License of `LocalStorage` 2.1.0 and `Mono.Posix.NETStandard`: not declared in package metadata; verify from their repositories.
6. Whether the ML Kit and other Google SDK terms allow redistribution of the APK outside Google Play. (They normally do, but I did not verify the current terms.)

## 10. Method and limits

Read: `git log`/`git show` of this repository and `upstream/main`; `LICENSE`, `app/LICENSE`, `NOTICE`, README; the GitHub
API record for `ggfto/DroidDeck`; `app/pubspec.lock` and license files in the local pub cache; nuspec files in the local
NuGet cache; the built APK and Windows publish folder. Not done: a full third-party text comparison of every transitive
license, current vendor terms (Google, Microsoft) fetched from the web, and any contact with the upstream author.
