# Changelog

All notable changes to KZ Companion are recorded here. Release entries are written from
[Conventional Commits](https://www.conventionalcommits.org/) by hand for each release; the release notes live in `docs/release-notes/`.

History of the upstream project this was forked from (DroidDeck 1.0 to 1.14.0, by Gabriel Freitas) is kept in
[CHANGELOG-upstream-DroidDeck.md](CHANGELOG-upstream-DroidDeck.md).

## Unreleased

### Changed

- KZ Companion is English only: the Portuguese, Spanish and Hindi translation files were removed and the text in the code is English.
- The PC configurator and pairing window use the new look.

### Added

- Android: swipe left or right to change section.
- Android: dashboard layout editor (12 x 6 grid; move, resize, add and remove widgets).
- AI section and widgets: Claude Code and Codex plan limits and token counts.
- Claude Code sessions on the AI page, with a banner and vibration when one finishes or waits for you.
- Dev section: GitHub CI status and Docker containers (start, stop, restart).
- Hardware: CPU and GPU clocks, video memory, and a high temperature warning.
- Switch the Windows default audio output from the phone; cover art on the Controls page.
- USB or Wi-Fi indicator, and an automatic switch to USB when the cable is plugged in.
- New look: ring gauges with a color per metric, AI pace verdicts, a large clock, a now-playing card, glow background.
- Full screen with page dots; the side bar is now an option in Appearance.
- Volume sliders, mute and power buttons, app tiles and the remaining cards use the new style.
- No top bar: connection status and layout editing sit in the bottom bar. Hardware fits one screen. "Running now" is below the saved apps.

### Fixed

- The Android app never chose the USB route: it probed an endpoint that no longer exists.
- A phone that failed `adb reverse` stopped the other phones from being set up, and a hung `adb` process was left running.

## 1.0.0 (2026-11-21)

KZ Companion 1.0.0 is the first release under the KZ Companion name. It replaces the DroidDeck button deck
with a dashboard and remote control for a Windows PC.

### Added

- Android Dashboard: live CPU, GPU and RAM, plus a Hardware page. Unsupported sensors (for example CPU temperature) are shown as unavailable.
- Windows audio mixer: master volume and mute, per-app volume and mute, microphone mute and system Deafen.
- Favorites and application launching, with installed application and game discovery (including Steam and Battle.net).
- Running Apps: Focus/Restore, Minimize and graceful Close, with per-app volume on the same row.
- Windows configurator: Applications, Integrations catalog and Appearance. Android Appearance settings.
- QR pairing with an API key. Update checks through GitHub Releases (Velopack).
- Integrations catalog with honest status labels. Setup screens for Discord, Spotify and OBS Studio; none verified against a live account yet.

### Changed

- Rebranded from DroidDeck to KZ Companion (see NOTICE).
- Removed the legacy button deck, profiles, automations and Stream Deck import.

### Known limitations

- Traffic between phone and PC is plain HTTP on the local network. No end-to-end encryption.
- The Windows installer is not code-signed.
- Apple Music support is unverified. Spotify, OBS and Discord have not been exercised against live services in release testing.
- CPU temperature and power need the optional PawnIO driver and administrator rights.
- The Android APK is ARM64-only and release-signed; it cannot be installed over a debug-signed build.
- The license of the Android app (`app/`) is not settled upstream (Apache-2.0 at the repository root, GPL-3.0 in `app/LICENSE`); see `NOTICE` and `docs/license-audit.md`.
