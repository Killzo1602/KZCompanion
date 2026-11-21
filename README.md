<div align="center">

<img src="app/assets/images/logo.png" alt="KZ Companion" width="112" height="112">

# KZ Companion

**Give an old Android phone a second life as a live dashboard and remote control for your Windows PC.**

[![Platform: Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=for-the-badge&logo=windows11&logoColor=white)](#installation)
[![Platform: Android](https://img.shields.io/badge/Android-companion%20app-3DDC84?style=for-the-badge&logo=android&logoColor=white)](#installation)
[![Latest release](https://img.shields.io/github/v/release/Killzo1602/KZCompanion?style=for-the-badge&label=Release&include_prereleases)](https://github.com/Killzo1602/KZCompanion/releases)
[![CI](https://img.shields.io/github/actions/workflow/status/Killzo1602/KZCompanion/ci.yml?branch=main&style=for-the-badge&label=CI)](https://github.com/Killzo1602/KZCompanion/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/github/license/Killzo1602/KZCompanion?style=for-the-badge)](LICENSE)

[**Download**](https://github.com/Killzo1602/KZCompanion/releases) ·
[**Installation**](#installation) ·
[**Documentation**](#documentation) ·
[**Report a bug**](https://github.com/Killzo1602/KZCompanion/issues/new/choose)

</div>

> [!NOTE]
> **Status: v1.0.0 is being prepared.** Check the [Releases page](https://github.com/Killzo1602/KZCompanion/releases)
> for published downloads. Until a release exists you can [build from source](#development).
> The Android APK is held back until an upstream licensing question is resolved; see
> [docs/release-checklist.md](docs/release-checklist.md).

---

## What it is

KZ Companion has two parts that work together:

- **KZ Companion for Windows** — a small tray app on your PC. It reads your hardware, controls the
  audio mixer, launches and manages apps, and talks to the integrations you choose to set up.
- **KZ Companion for Android** — the dashboard and remote control. It shows live PC stats and gives you
  touch controls for audio, apps and more.

The phone you stopped using, propped up next to your monitor, becomes a dedicated PC control panel:
CPU, GPU and RAM at a glance, a per-app volume mixer under your thumb, and your favorite apps one tap away.

KZ Companion is a modified fork of [DroidDeck](https://github.com/ggfto/DroidDeck) by Gabriel Freitas.
See [Credits and license](#credits-and-license).

## Features

**Monitoring**
- Live CPU, GPU and RAM usage with history on the Android Dashboard, plus a Hardware page with more detail.
- Sensor values such as CPU temperature and power are shown **only when your hardware and the sensor driver
  report them**; otherwise the app says they are unavailable. See [CPU temperature is missing](#cpu-temperature-is-missing).

**Audio**
- Windows master volume and mute.
- Per-application volume and mute, from the Windows audio mixer.
- Microphone mute and a system-wide **Deafen** (see [below](#windows-microphone-controls-vs-discord-mute)).

**Apps**
- **Favorites** and one-tap application launching.
- **Installed application and game discovery**, including Steam and Battle.net games, in the app picker.
- **Running Apps**: the applications that currently have a window, one row per app, with
  **Focus / Restore**, **Minimize** and **graceful Close** (the same as clicking the window's X; it never
  force-kills a process, and the phone asks before closing). Per-app volume appears on the same row when the app has an audio session.

**Customization and connection**
- Appearance settings on both Windows and Android.
- **QR pairing** and authenticated communication between phone and PC.
- An optional **integrations catalog** (see the [support matrix](#integrations)).

### Windows microphone controls vs. Discord mute

These are two different things, and KZ Companion does not blur them.

| | Windows mic mute / Deafen | Discord mute / deafen |
|---|---|---|
| Works on | The default Windows microphone and output device | Your voice state **inside Discord** only |
| Needs setup | No | Yes: the Discord integration (see below) |
| Effect | Every app stops hearing the mic (mute). Deafen also mutes the default output. | Discord's own self-mute/self-deafen |
| Deafen restore | Turning Deafen off restores each device to the mute state it had before. | — |

Muting the Windows microphone silences it for all applications, including Discord. It does not change
Discord's own mute button state. The Discord integration is what controls Discord's internal state.

## Integrations

Integrations are **optional** and are configured **on the Windows PC**. The catalog is a list of what
exists, with an honest status. **Nothing below has been verified against a live account or device yet**;
"setup available" means the setup screen and backend exist, not that it is proven in every environment.

| Integration | Status | Notes |
|---|---|---|
| **Discord** | Setup available | Mute/deafen, voice channel, volume via Discord's local RPC. You create your own Discord application. Discord restricts the voice scopes to approved apps; unapproved apps work for accounts on the app's tester list (up to 50). The Discord soundboard is not offered. |
| **Spotify** | Setup available | Account sign-in works; the player panel is not in the app yet. Needs Premium and your own Spotify developer app (Development Mode: Premium owner, up to 5 users). "Save to library" uses a Spotify endpoint removed in Feb 2026 and needs an update. |
| **OBS Studio** | Setup available | Connects through OBS WebSocket 5.x and reads state. Phone controls are not built yet. |
| **Apple Music** | Candidate | Not verified. See [Apple Music on Windows](#apple-music-on-windows). |
| **Home Assistant** | Backend only | Code exists; no KZ setup screen or device list yet. |
| **Philips Hue** | Backend only | Uses the older local Hue API v1; its long-term support is unverified. |
| **Govee (LAN)** | Backend only | Needs LAN Control on each device. |
| **MQTT** | Backend only | Publish only. |
| **Twitch** | Backend only | Needs your own Twitch application. |
| **YouTube Live** | Backend only | Needs your own Google Cloud OAuth client. |
| **Tuya / Smart Life** | On hold | Pairing currently borrows Home Assistant's public registration; off until KZ Companion has its own. |

Status meanings: **Setup available** = a setup screen exists. **Backend only** = the PC has code but there is
no KZ screen. **Experimental / unverified** = anything that has not been run against the real service
(currently every row). **Candidate** = a plausible idea with no proof. **On hold** = deliberately paused.

The PC code also contains older integrations inherited from DroidDeck (for example Voicemeeter, Elgato,
Streamer.bot, VTube Studio, vMix, Bitfocus Companion, radio automation, a MyInstants soundboard). They have
no KZ interface and are neither supported nor tested in KZ Companion. The detailed audit is in
[docs/integration-audit-phase1.md](docs/integration-audit-phase1.md).

### Apple Music on Windows

There is no public API to control the Apple Music desktop app. KZ Companion's only possible route is the
**Windows media session** (the same system media controls Windows shows for playing media), and it has
**not been tested** whether Apple Music for Windows exposes one. Even if it does:

- play/pause/skip depend on what the app allows through that interface;
- there are no playlists, likes or library access;
- per-app volume would come from the Windows mixer, independent of media sessions.

MusicKit does not apply: it plays music inside your own app and cannot control the desktop app.

## Installation

You need **both** parts: the Windows app and the Android app. The phone and the PC must be on the same
local network (or connected by USB with Android debugging).

> [!NOTE]
> Downloads are on the [Releases page](https://github.com/Killzo1602/KZCompanion/releases) once v1.0.0 is published.
> The files are `KZCompanion-Setup-1.0.0.exe` (Windows x64), `KZCompanion-Android-arm64-v1.0.0.apk` (Android ARM64)
> and `SHA256SUMS.txt`. Check the download with `Get-FileHash <file> -Algorithm SHA256`.

**Requirements:** Windows 10 version 1809 (build 17763) or newer on the PC; an Android phone. The Windows
app is self-contained, so you do not install .NET separately. The Windows installer is not code-signed
yet, so SmartScreen may warn on first run (see [Troubleshooting](#troubleshooting)).

1. **Windows.** Download `KZCompanion-Setup-1.0.0.exe` and run it. It installs for your user (no administrator rights
   needed), runs from the system tray and updates itself from GitHub Releases.
2. **Android.** Download `KZCompanion-Android-arm64-v1.0.0.apk` on the phone (ARM64 devices only), allow installing from that source when Android asks,
   and open it.
3. **Allow the firewall.** On first launch the PC app offers to open the needed ports in Windows Firewall
   (TCP 4787 for the app, UDP 7573 for discovery). Accept for private networks.
4. **Pair with QR.** On the PC, right-click the tray icon and open the pairing window. In the Android app,
   scan the QR code. The phone stores the key and connects.
5. **Connect.** Once paired, the Dashboard fills with live data. The PC also answers a discovery broadcast
   (UDP 7573) on the local network. USB (cable) connection is available from the tray menu and needs USB debugging on the phone.
6. **Set up integrations (optional).** On the PC, choose **Open KZ Companion** in the tray menu (the
   configurator, served at `http://localhost:4787`) and go to **Integrations**. Enter credentials **on the PC**, not on the phone.

## Security and privacy

- **Local only.** The phone talks directly to the PC over your local network. There is no KZ cloud
  service and no account. The app and its integrations do make normal outbound calls when you enable
  them (for example Spotify, Discord sign-in, GitHub for update checks), so KZ Companion is not "fully offline".
- **Pairing and authentication.** The PC generates a random API key on first run. The QR code carries
  the PC address and that key. Every API call and the live connection require the key.
  Anyone holding the key can control the PC through KZ Companion, so treat it like a password and
  do not share screenshots of the QR code.
- **Plain HTTP on your LAN.** Traffic between phone and PC is **not encrypted** (HTTP and WebSocket, not
  HTTPS) and there is **no end-to-end encryption**. Use it on networks you trust, not on public Wi-Fi.
- **Secrets at rest.** The API key and integration credentials are stored under `%LocalAppData%\KZCompanion`
  encrypted with Windows DPAPI for your user account. That protects them from other Windows users, **not**
  from other programs running as you.
- **Privileged actions.** Actions that run code on the PC are limited to the local PC and are not available
  to remote phone connections. Credentials for integrations are meant to be entered on the PC.
- **Known limitation.** Today any paired phone can still change some integration settings; moving all
  credential entry to the PC is planned work. See [SECURITY.md](SECURITY.md) to report a vulnerability.

## Troubleshooting

### Android cannot connect
Make sure the phone and PC are on the same network (not a guest network with client isolation) and
KZ Companion is running in the tray. Re-pair if the PC was reinstalled or its key was reset.

### QR pairing does not work
Check camera permission for the app, raise screen brightness and avoid glare. Make sure the pairing window
was opened from the tray (**Pair device (QR)**) on the PC you want to control. A VPN on either device can
route traffic away from the LAN, and a guest or "client isolation" Wi-Fi blocks phone-to-PC traffic.

### Windows Firewall
KZ Companion needs TCP 4787 and UDP 7573 on **private** networks. If you declined the prompt, use the tray
menu item **Allow in firewall (local network)**, or add inbound rules for those ports.

### Running Apps permissions
Running Apps only uses window and process information that Windows exposes to a normal user. Windows may
refuse to bring a window to the front (the phone shows a message); try again, or click the window once.

### Elevated applications
Applications running **as administrator** are listed as locked. Windows does not let a normal process
control them, so every action is refused. Start KZ Companion as administrator if you need to manage them.
Apps that hide to the tray when closed lose their row, and windows on other virtual desktops are hidden.

### CPU temperature is missing
Reading CPU temperature needs a sensor driver and often administrator rights. If the Hardware page says it
is unavailable, use the tray menu: *Install CPU sensor driver*, then *Restart as administrator*. Some CPUs
do not expose the sensor at all; then it stays unavailable and KZ Companion will not invent a value.

### Integration setup fails
Read the requirements on the integration's own page. The common ones: Discord needs its desktop app running
and your account on the app's tester list; Spotify needs Premium and the exact redirect address
`http://127.0.0.1:4787/api/integrations/spotify/callback`; OBS needs its WebSocket server enabled and the
right port and password. The log is at `%LocalAppData%\DroidDeck.log` (the file name predates the rename).

### Windows media-session compatibility
Media information and controls come from the Windows media session. Apps that do not publish one (or
publish an incomplete one) will not show now-playing data or respond to play/pause. See
[Apple Music on Windows](#apple-music-on-windows).

### SmartScreen warning
The installer is not code-signed yet. Choose **More info**, then **Run anyway**, only if you downloaded it
from this repository's Releases page.

## Development

### Repository layout

| Path | Contents |
|---|---|
| `DroidDeck/` | Windows tray app and local server (C#, .NET 8, ASP.NET Core + SignalR). Project is named `DroidDeck`, assembly `KZCompanion`. |
| `app/` | Android app and the web configurator (Flutter/Dart, one code base). |
| `tests/DroidDeck.Tests/` | Backend tests (xUnit). |
| `scripts/` | `publish-windows.ps1` (verified Windows publish), `deploy-web.ps1`. |
| `docs/` | Design notes, integration audit, release checklist. |
| `website/` | Inherited DroidDeck documentation site. Not yet rewritten for KZ Companion; see its README. |
| `.github/` | CI, release workflow, issue and PR templates. |

### Tools

- Windows 10/11, **.NET 8 SDK** (the project targets `net8.0-windows10.0.17763.0`).
- **Flutter** stable with Dart 3.8 or newer (`app/pubspec.yaml`).
- Android: a JDK 17 and the Android SDK for APK builds.
- Optional: Node 20 (release tooling only).

### Build

```powershell
# Windows app, verified publish: fresh web build, clean wwwroot, new output folder
.\scripts\publish-windows.ps1 -Out dist\KZCompanion-win-x64-local

# Run from source (tray app + server on http://localhost:4787)
dotnet run --project DroidDeck

# Android
cd app
flutter pub get
flutter build apk --release
```

The configurator front-end is served from `DroidDeck\wwwroot`, which is git-ignored and goes stale
silently. Always use `publish-windows.ps1`: it rebuilds the web app, replaces `wwwroot`, never overwrites a
running executable, and fails if legacy deck UI text is found or the published front-end is not the fresh one.

### Tests

```powershell
dotnet test DroidDeck.sln -c Release --filter "Category!=RequiresDesktop"
cd app; flutter analyze --no-fatal-infos; flutter test
```

Tests marked `RequiresDesktop` need an interactive desktop (keyboard hooks) and run locally only.
If `dotnet test` aborts with *"You must install or update .NET"* asking for `Microsoft.NETCore.App 8.0.34`,
your machine has the ASP.NET Core 8.0.34 runtime without the matching .NET 8.0.34 runtime. Install the
current .NET 8 runtime (or SDK), or temporarily set `DOTNET_ROLL_FORWARD=Major`. Details in
[docs/release-checklist.md](docs/release-checklist.md).

### Release publishing

Releases come from the manual **Release** workflow (`.github/workflows/release.yml`, input: the version). It refuses to run
without the release-signing secrets, runs the tests, builds the verified Windows installer (Velopack) and the
release-signed ARM64 APK, checks the front-end and signature, writes `SHA256SUMS.txt`, creates a draft release,
pushes the annotated tag and then publishes. Do not tag or publish by hand; follow
[docs/release-checklist.md](docs/release-checklist.md). Signing is described in [docs/android-signing.md](docs/android-signing.md).

## Documentation

- [Release checklist](docs/release-checklist.md), [Android signing](docs/android-signing.md), [License audit](docs/license-audit.md)
- [Integration audit](docs/integration-audit-phase1.md) and [Running Apps design](docs/running-apps-phase2.md)
- [Changelog](CHANGELOG.md)

## Community

- **Bugs and feature requests:** [GitHub Issues](https://github.com/Killzo1602/KZCompanion/issues/new/choose).
  For a bug, include your Windows version, Android version and device model, the KZ Companion version,
  steps to reproduce, what you expected and what happened, and relevant logs or screenshots.
  **Never upload credentials, pairing keys, QR codes or tokens.**
- **Contributing:** see [CONTRIBUTING.md](CONTRIBUTING.md).
- **Security issues:** see [SECURITY.md](SECURITY.md).

## Credits and license

KZ Companion is a modified fork of **[DroidDeck](https://github.com/ggfto/DroidDeck)** by Gabriel Freitas. Upstream
copyright notices and license files are preserved; [NOTICE](NOTICE) lists the changes.

| Part | License | Status |
|---|---|---|
| Windows app (`DroidDeck/`), tests, scripts, docs | [Apache License 2.0](LICENSE), as declared by the upstream author | Upstream's declaration; KZ Dev Studio's additions are offered under the same terms |
| Android app and web configurator (`app/`) | **Not settled.** The upstream repository ships both the Apache-2.0 root `LICENSE` and a GPL-3.0 [`app/LICENSE`](app/LICENSE) | Unconfirmed by the upstream author. See [docs/license-audit.md](docs/license-audit.md). |
| Third-party components | Their own licenses (MIT, BSD, Apache-2.0, MPL-2.0; Google ML Kit under Google's terms) | [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |

**This repository is not claimed to be entirely Apache-2.0.** Because of the open question about `app/`, the Android APK
is only published once that question is resolved (see the [release checklist](docs/release-checklist.md)).

Hardware sensors use [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0).
This project is unrelated to [Droid-Deck/DroidDeck](https://github.com/Droid-Deck/DroidDeck), a different
app that runs Steam games on Android.
