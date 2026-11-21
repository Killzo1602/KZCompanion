# KZ Companion — Phase 1: Integration Audit & Architecture

Baseline: `546ed96` · Date: 2026-11-21 · Scope: audit and planning only. No code changed.

**Evidence labels used throughout**

- **[code]** read from the source in this repository.
- **[docs]** checked against current official documentation (fetched 2026-11-21).
- **[untested]** nothing was run against a live service, device or app. No integration was exercised in this phase.

No credentials were requested, read or exposed. No application was launched.

---

## 0. Executive summary

1. **The backend still carries the whole DroidDeck integration layer** (19 integrations, about 12.7k lines under `DroidDeck/Integrations`). It compiles and is registered in DI (`DroidDeck/ServiceRegistration.cs:86`). Only Discord has a UI in the KZ app (`app/lib/src/kz/kz_integrations_page.dart`).
2. **The biggest hidden problem: most "live state" code is gated on legacy deck buttons.** Spotify, Twitch and YouTube poll only if some profile contains a button of that type (`ActionsOfType(...)`). Home Assistant publishes only the entities used by buttons (`HomeAssistantIntegration.cs:85`). KZ has no buttons, so **these integrations would connect but never publish state**. They need a "who is interested" signal that does not depend on buttons.
3. **Two documented API breaks affect existing code:**
   - Spotify removed `PUT/DELETE /me/tracks` in Feb 2026. "Like" now needs `/me/library` [docs]. `SpotifyIntegration.SetLikedAsync` and `IsLikedAsync` will fail on the removed endpoints.
   - Discord's `rpc.*` scopes are **approved-partners only**. An unapproved app works only for users on the app's tester list (up to 50) [docs]. The code comment "works for the app owner" is not confirmed by Discord's docs.
4. **Discord soundboard RPC commands are not documented.** `GET_SOUNDBOARD_SOUNDS` and `PLAY_SOUNDBOARD_SOUND` are absent from the RPC command table [docs]. Do not expose them.
5. **Tuya pairing impersonates the Home Assistant app registration** (`client_id=HA_3y9q4ak7g4ephrvke`, `docs/tuya-business-review.md`). That is fragile and not something KZ should present as a feature. Classified obsolete or unsafe until it has its own Tuya registration.
6. **Apple Music is installed on this PC** (`AppleInc.AppleMusicWin 1.1792.39942.0`) but was not running, so it was not launched. Whether it appears as a Windows media session is **unknown**. The existing SMTC layer (`MediaControlService`) is the right place to find out. A 10-minute live probe is the first task of the Apple Music phase.
7. **Running Apps is a small, low-risk first deliverable.** `GET /api/system/apps` (`LibraryController.cs:28`) is a thin wrapper over `ForegroundWatcher.RunningApps()`. It returns one `value/label` pair per process name. It has no PID, HWND, path, icon or actions. It does not use the data the audio mixer already collects (PID, path, icon, group key).

---

## 1. Integration inventory

### 1.1 Shared infrastructure [code]

| Piece | File | Notes |
|---|---|---|
| `IIntegration` | `Integrations/IIntegration.cs` | `Name`, `Title`, `IsConnected`, `AccountName`. No capability model, no config schema. |
| `AccountIntegration` | `Integrations/AccountIntegration.cs` | `BackgroundService` base. Per-integration store at `%LocalAppData%\DroidDeck\{name}.json`, DPAPI-encrypted through `SecretFile` (`Lib/SecretFile.cs`). Exposes `GetStatus/GetState/ConfigureAsync/LogoutAsync/CallbackAsync/OptionsAsync`. |
| `OAuthTokens`, `ApiCall` | same file | PKCE and device-code helpers, token refresh under a lock, a single retry after 401. |
| `IntegrationsController` | `Integrations/IntegrationsController.cs` | `GET /api/integrations` (list), `GET/POST/DELETE /api/integrations/{name}`, `GET .../options/{list}`, `GET .../callback` (`[AllowAnonymous]`, validated by OAuth `state`). |
| Legacy controllers | `Integrations/Discord/DiscordController.cs`, `Obs/ObsController.cs`, `Tuya/TuyaController.cs`, `Govee/GoveeController.cs`, `Soundboard/SoundboardController.cs` | Older per-integration REST. They do not use `AccountIntegration`. |
| Action providers | `Actions/IActionProvider.cs`, `ServiceRegistration.cs:45` | `IStatefulActionProvider` supplies `StateEvent` and `GetState()`. `DeckHub.OnConnectedAsync` replays each provider's state to a new client (`Hubs/DeckHub.cs`). |
| Auth | `Auth/ApiKeyAuthenticationHandler.cs`, `Program.cs:255` | One API key for all clients, as `X-API-KEY` or `?access_token=` for SignalR. The authorization fallback policy requires it everywhere except `[AllowAnonymous]` routes. |
| Privilege gate | `Services/ActionPolicy.cs` | `IsLocal(connection)` is loopback and not a USB-forwarded connection. Used for code-execution actions and for the library-editing endpoints. |

### 1.2 Per-integration audit

SignalR event names are the server-to-client methods. Every state event is broadcast with `Clients.All`.

#### Discord — `Integrations/Discord/*`
- **Implementation [code]:** `DiscordRpcService` talks to the local Discord client over the IPC named pipe (`discord-ipc-0..9`). It does handshake, `AUTHORIZE`, a token exchange at `discord.com/api/oauth2/token`, then `AUTHENTICATE`. `DiscordAutoConnect` is a 10 s watchdog.
- **Commands [code]:** mute and deafen (`SET_VOICE_SETTINGS`), input and output volume, voice mode (PTT or VAD), per-user mute and volume (`SET_USER_VOICE_SETTINGS`), `GET_GUILDS`, `GET_CHANNELS`, `SELECT_VOICE_CHANNEL`, `GET_SELECTED_VOICE_CHANNEL`, soundboard.
- **State [code]:** self mute and deaf, voice channel and guild, participants, input and output volume, mode. Subscribes to `VOICE_SETTINGS_UPDATE` and `VOICE_CHANNEL_SELECT`.
- **Auth/config [code]:** the user creates their own Discord application. Client ID and Secret are stored in `discord.json` (DPAPI). Scopes are `rpc rpc.voice.read rpc.voice.write`. Redirect URI is `http://localhost:4787/discord`.
- **HTTP:** `POST /api/discord/config|connect|mute|deafen|play-soundboard`, `GET /api/discord/state|guilds|channels/{id}|voice-channel|soundboard-sounds`.
- **SignalR:** `ReceiveDiscordState`.
- **DroidDeck dependencies:** `DiscordProvider` (deck action). The service itself is standalone.
- **Compatibility [docs]:**
  - IPC transport and all voice commands used are documented and current. Only the WebSocket transport is deprecated.
  - `rpc`, `rpc.voice.read` and `rpc.voice.write` are **"only available to approved partners"**. Unapproved apps work only for users on the app's tester list (50 slots). Requesting without approval "may cause errors or undocumented behavior".
  - Voice settings have a **single-modifier lock**: while KZ holds them, other RPC apps cannot change them, and the settings revert when KZ disconnects. Discord marks this as temporary.
  - Token lifetime is 7 days. The code already refreshes with the refresh token and clears tokens on `invalid_grant`.
  - Discord says the SDK is preferred for new projects. That is a recommendation, not a deprecation of RPC.
- **Missing:** a setup flow that tells the user about the tester requirement. Soundboard uses undocumented commands (see §2). There is no `rpc.notifications` or screen-share handling, which is not needed.
- **Security/maintenance:**
  - The client secret travels from the phone to the PC today (`kz_integrations_page.dart` form). It is never sent back (the state exposes only `clientId`). Prefer entering it on the PC.
  - `DiscordController.Config` has no `IsLocal` check, so any paired phone can overwrite the credentials.
  - The Discord UI page shows "Connected" but not the voice controls. Nothing in the KZ app calls mute or deafen yet.
- **Class: 1 — Ready to expose**, with the approval or tester caveat. The soundboard part is class 4.

#### OBS Studio — `Integrations/Obs/*`
- **Implementation [code]:** `ObsService` is an obs-websocket 5.x client (`ws://host:4455`). It does Hello, then Identify with the SHA-256 challenge, then requests and events. `ObsAutoConnect` is a 10 s watchdog.
- **Commands [code]:** set scene, toggle or start or stop record, pause, chapter, toggle stream, virtual cam, replay buffer, save replay, input mute, input volume, scene items, filters, screenshot, studio mode.
- **State [code]:** current and preview scene, scene list, record, pause, stream, virtual cam, replay buffer, audio inputs. Event-driven (`CurrentProgramSceneChanged`, `RecordStateChanged`, `StreamStateChanged`, ...).
- **Auth/config [code]:** host, port, password in `obs.json` (DPAPI).
- **HTTP:** `POST /api/obs/config|connect`, `GET /api/obs/state|scenes|sources|scene-items|filters|audio-inputs`.
- **SignalR:** `ReceiveObsState`. Already parsed by the app (`signalr_service.dart:621`).
- **Compatibility [docs]:** Hello, Identify (`rpcVersion: 1`) and SHA-256 auth match the 5.x protocol. Identify omits `eventSubscriptions`, which gives the default non-high-volume set, so volume meters are not streamed. That is correct. I could not confirm anything specific to OBS 32.
- **Missing:** the actions themselves have no REST route (they exist only as `ObsProvider` deck actions), so KZ needs thin endpoints for `SetScene`, `ToggleRecord`, `ToggleStream` and the like. Also needed: a "disconnect when unconfigured" rule (see below) and real error text for auth failure. A wrong password currently surfaces as "OBS did not respond (handshake)".
- **Security/maintenance:** `ObsAutoConnect` retries every 10 s whenever `obs.json` exists, even when OBS is closed and no phone is connected. It is cheap but it is constant background activity.
- **Class: 1 — Ready to expose.**

#### Spotify — `Integrations/Spotify/*`
- **Implementation [code]:** OAuth 2 with PKCE (Client ID only, no secret) through `AccountIntegration`. Redirect is `http://127.0.0.1:4787/api/integrations/spotify/callback`.
- **Commands [code]:** play or pause, next, previous, shuffle, repeat, volume, play playlist, like. Playlists come from `OptionsAsync("playlists")`.
- **State [code]:** track, artist, cover (base64), playing, liked, shuffle, repeat, volume, device. Polled every 3 s.
- **HTTP:** `/api/integrations/spotify` (+ `/options/playlists`, `/callback`).
- **SignalR:** `ReceiveSpotifyState`. **The app does not listen for it yet** (`signalr_service.dart` has no handler).
- **Compatibility [docs] (Feb 2026 migration guide):**
  - OK, unchanged: `GET /me/player`, `PUT /me/player/play|pause|shuffle|repeat|volume`, `POST /me/player/next|previous`, `GET /me/playlists`, `GET /me`. Redirect `http://127.0.0.1` is allowed (`localhost` is not).
  - **Broken:** `PUT/DELETE /me/tracks` and `GET /me/tracks/contains` were removed. Replacement is `PUT/DELETE /me/library` with `{"uris":[...]}` and `GET /me/library/contains`. The "like" feature and `IsLikedAsync` in `SpotifyIntegration.cs` will fail. The calls need updating (code, not a design change).
  - `GET /me` no longer returns `product`, so Premium cannot be detected from the profile. Handle `403 PREMIUM_REQUIRED` from the player endpoints instead.
  - **Development Mode** (every app a user creates themselves): the app owner needs an active **Premium** subscription, with a cap of 5 authorized users per app. Extended Quota is **not available to individuals** (organizations only, 250k MAU). KZ's "bring your own Client ID" model stays viable for a personal tool but cannot be offered as a one-click login to the public.
  - Playback control requires Premium regardless.
- **Missing:** the polling gate (see §0.2), SignalR handling in the app, seek, queue and device transfer (not required for v1). No `retry-after` handling for HTTP 429.
- **Security:** tokens in `spotify.json` (DPAPI). The callback is anonymous but protected by a one-shot `state` and PKCE. Good.
- **Class: 2 — Reusable with modifications.** It needs the `/me/library` fix, the polling gate and a SignalR listener.

#### Twitch — `Integrations/Twitch/*`
- **Implementation [code]:** Helix with the Device Code flow (public client, no secret). Scopes: `channel:manage:broadcast channel:edit:commercial clips:edit channel:manage:polls user:write:chat`. Live and viewer count polled every 30 s.
- **Commands [code]:** marker, clip, chat message, poll, set title (see `TwitchProvider.cs`).
- **HTTP:** `/api/integrations/twitch`. **SignalR:** `ReceiveTwitchState`.
- **Compatibility [docs]:** Device Code Flow is a supported public-client flow. I found no 2026 change affecting it. I did not re-verify every scope against the current scopes table.
- **Missing:** the polling gate (button-dependent). It is a streamer feature outside the stated roadmap.
- **Class: 2 — Reusable with modifications** (low priority).

#### YouTube — `Integrations/YouTube/*`
- **Implementation [code]:** Data API v3 with the user's own "Desktop app" OAuth client. Polls the live broadcast every 60 s, can send chat messages.
- **Concerns:** the daily quota is small. The poll is button-gated like Twitch. Per Google policy the user must create their own project and consent screen.
- **Class: 2 — Reusable with modifications** (low priority, not verified against current Google docs in this phase).

#### Home Assistant — `Integrations/HomeAssistant/*`
- **Implementation [code]:** WebSocket API with a long-lived access token. Does `auth`, `get_states`, `subscribe_events(state_changed)` and `get_config`. Reconnects with a back-off up to 60 s. Idle when not configured (1-day wait).
- **Commands [code]:** `call_service` for light, switch, scene, script, cover and others (`HomeAssistantProvider`).
- **State [code]:** all entities are cached in memory, but only entities referenced by buttons are sent to clients (`UsedEntities()` via `ActionsOfType`).
- **HTTP:** `/api/integrations/homeassistant` (+ `/options/{list}` for entities).
- **SignalR:** `ReceiveHomeAssistantState`.
- **Compatibility [docs]:** the auth handshake and event subscription match the documented WebSocket API. I did not do a full command-by-command check against the current HA docs.
- **Missing:** a user-chosen device list stored by KZ (replaces "entities used by buttons"). Area and device grouping. No TLS-certificate option for self-signed HTTPS (`ClientWebSocket` default validation).
- **Security:** the long-lived token is stored in `homeassistant.json` (DPAPI). The URL is accepted as typed (`http` allowed), so any paired phone can point the PC at any LAN address. Restrict configuration to the PC.
- **Class: 2 — Reusable with modifications.**

#### Philips Hue — `Integrations/Hue/*`
- **Implementation [code]:** local **API v1** with a link-button pairing. Bridge discovery via `https://discovery.meethue.com/` (a Signify cloud service). Plain HTTP on the LAN. Wakes every 1 or 5 minutes to check the bridge, but only when configured.
- **Missing:** API v2 and HTTPS (Signify is moving to v2 and HTTPS). I did not verify the v1 end-of-life status, so this is a **risk to verify**. Event stream, entertainment.
- **Class: 2 — Reusable with modifications** (verify v1 support before investing).

#### Govee — `Integrations/Govee/*`
- **Implementation [code]:** LAN UDP API (multicast `239.255.255.250:4001`, replies on 4002, commands on 4003). No account, no cloud. Needs "LAN Control" enabled on each device.
- **Concerns:** binds UDP 4002 and fails if another program holds it. Per-button IP, with no stored device list. There is no persistent listener when unconfigured.
- **Class: 2 — Reusable with modifications.** Needs a stored device list.

#### Tuya / Smart Life — `Integrations/Tuya/*`
- **Implementation [code]:** QR "device-sharing" pairing through `apigw.iotbing.com`, a signed and encrypted request pipeline, and push state over Tuya MQTT. No polling (free-tier API quota is about 26k calls a month).
- **Concerns:**
  - It reuses the **public Home Assistant registration** (`HA_3y9q4ak7g4ephrvke`, `haauthorize`). The authorization screen shows "Home Assistant". This depends on a third party's registration and mimics another product's client. `docs/tuya-business-review.md` is a draft request for KZ's own registration.
  - Brand apps (Nova Digital, Positivo, RSmart) are rejected by the schema.
  - `TuyaCrypto` and the signing are reverse-engineered, not documented by Tuya.
- **Class: 4 — Obsolete or unsafe** in its current form. Do not expose until KZ has its own Tuya registration. Keep it dormant.

#### MQTT — `Integrations/Mqtt/*`
- **Implementation [code]:** MQTTnet 4.3, one persistent connection to a configured broker, **publish only**. TLS via `mqtts://`. Idle when unconfigured.
- **Missing:** subscribe and state (needed for any "device state" display), retain and QoS options, certificate options.
- **Class: 2 — Reusable with modifications.** Useful as a generic smart-home bridge later.

#### Other integrations found [code]

| Integration | Files | Summary | Class |
|---|---|---|---|
| Voicemeeter | `Voicemeeter/*` | Loads `VoicemeeterRemote64.dll` from the user's install. Polls only when a client is connected (`TallyService`). | 2 |
| Elgato Key Light | `Elgato/KeyLightProvider.cs` | Local HTTP (port 9123), IP typed per button. | 2 |
| Streamer.bot | `StreamerBot/*` | WebSocket, `DoAction`, optional password. | 2 (low) |
| VTube Studio | `VTubeStudio/*` | Public WebSocket API with plugin token. | 2 (low) |
| vMix | `Vmix/*` | TCP API on 8099. | 3 (out of scope) |
| Bitfocus Companion | `Companion/*` | Phones act as a Satellite surface (TCP 16622). Tied to the legacy button grid. | 4 (obsolete for KZ) |
| mAirList, RadioDJ | `Radio/*` | Radio-automation REST. RadioDJ puts the password in the URL. | 3 (out of scope) |
| Soundboard | `Soundboard/*` | Searches MyInstants through an **unofficial JSON wrapper**, plays MP3 into an output device. | 4 (unofficial third-party) |
| HTTP / Network / Script actions | `Actions/Providers/HttpProvider.cs` etc. | Generic actions. Already marked privileged in `ActionPolicy`. | n/a (not integrations) |
| Timer, Steam, Windows, Media, Mixer | `Actions/Providers/*` | Deck actions, not external integrations. | n/a |

Only the integrations listed above exist. No Twitch EventSub, no Philips Hue v2, no Nanoleaf and no LIFX code exists.

### 1.3 Summary table

| # | Integration | Class | Main blocker |
|---|---|---|---|
| 1 | Discord (voice) | **1** | Discord approval or tester slot, user-created app |
| 2 | OBS Studio | **1** | Add action endpoints, no blocker otherwise |
| 3 | Spotify | **2** | `/me/library`, button-gated polling, no app listener |
| 4 | Home Assistant | **2** | Button-derived entity list |
| 5 | Twitch | **2** | Button-gated polling |
| 6 | YouTube | **2** | Button-gated polling, quota |
| 7 | MQTT | **2** | Publish-only |
| 8 | Hue | **2** | API v1 (verify) |
| 9 | Govee LAN | **2** | No stored device list |
| 10 | Voicemeeter, Elgato, Streamer.bot, VTube Studio | **2** | Per-button config |
| 11 | vMix, mAirList, RadioDJ | **3** | Out of scope |
| 12 | Apple Music (new) | **3** | Unknown SMTC behavior, see §3 |
| 13 | Tuya | **4** | Borrowed HA registration |
| 14 | Companion | **4** | Legacy grid |
| 15 | Soundboard | **4** | Unofficial wrapper, undocumented Discord soundboard commands |

---

## 2. Compatibility assessment

| Area | Verdict | Basis |
|---|---|---|
| Discord IPC and voice commands | Current, documented | [docs] |
| Discord rpc scopes | **Restricted** (approved partners or tester list) | [docs] |
| Discord soundboard RPC | **Not documented**, do not rely on it | [docs] |
| Discord single-modifier lock | Applies, document it for users | [docs] |
| Spotify player endpoints | Current | [docs] |
| Spotify like and saved tracks | **Broken by Feb 2026 change** | [docs] |
| Spotify Dev Mode limits | Premium owner, 5 users, no Extended Quota for individuals | [docs] |
| OBS obs-websocket 5.x | Matches protocol | [docs], OBS 32 not confirmed |
| Twitch device code | Supported flow, scopes not re-verified | [docs] partial |
| Home Assistant WebSocket | Matches handshake | [docs] partial |
| Hue v1, Govee LAN, Tuya, MQTT | Not verified against current vendor docs | [code] only |

**Not tested:** no integration was connected to a live service during this audit. "Ready" means "the implementation reads correctly and matches documentation", not "verified working". Every phase below includes a live test checklist.

**Cross-cutting code findings**

| ID | Finding | Where | Impact |
|---|---|---|---|
| F1 | State publishing depends on deck buttons | `AccountIntegration.ActionsOfType`, `SpotifyIntegration.cs:180`, `TwitchIntegration.cs:180`, `YouTubeIntegration.cs:157`, `HomeAssistantIntegration.cs:85` | No state in KZ without buttons |
| F2 | Integration configuration is reachable by any paired phone | `IntegrationsController` (no `IsLocal`), `DiscordController.Config` | A leaked or shared key can reconfigure or repoint integrations. Secrets are not readable back. |
| F3 | Always-on loops | `ObsAutoConnect` (10 s while `obs.json` exists), `DiscordAutoConnect` (10 s while tokens exist), Spotify/Twitch/YouTube (wake every 2–3 s even if unconfigured), Hue (1–5 min), ~25 hosted services | Wasteful on a "lightweight" companion |
| F4 | No capability description | `IIntegration` | The app cannot know what controls to show |
| F5 | One shared API key over plain HTTP on the LAN | `Auth/*`, `Program.cs:105` | Existing pairing model; out of scope to change, but keep in mind for secrets and remote actions |
| F6 | App lacks handlers for most state events | `signalr_service.dart` | Needs `ReceiveSpotifyState` and similar |
| F7 | Two UI-facing config styles coexist | legacy controllers vs `AccountIntegration` | The catalog needs one uniform status shape |

---

## 3. Apple Music feasibility

**Environment [code/system]:** the Microsoft Store app `AppleInc.AppleMusicWin` v1.1792.39942.0 is installed on the dev PC. It was not running and was not launched. **No live SMTC probe was done.**

| Capability | Mechanism | Status |
|---|---|---|
| Detect running | Process or window enumeration (Running Apps service) | **Feasible** (any app) |
| Now playing and metadata | SMTC `TryGetMediaPropertiesAsync` through the existing `MediaControlService` (`GetAllSessionsAsync`, `GetNowPlayingAsync`) | **Unknown.** SMTC is the documented, supported way for a Windows media app to expose this [docs], but Microsoft's docs do not say which apps register. Whether Apple Music does, and under which `SourceAppUserModelId`, must be tested. |
| Play/pause, next, previous | SMTC `TryPlay/TryPause/TrySkipNext/TrySkipPrevious` (`MediaControlService.SendCommandAsync`) | **Unknown**, same dependency. `Try*` methods are requests that an app may refuse. The existing `CanPlayPause/CanGoNext/CanGoPrevious` flags show what the app actually allows. |
| Seek, shuffle, repeat | SMTC (`TryChangePlaybackPositionAsync`, shuffle and repeat) | Unknown. Treat as optional. |
| Thumbnail | SMTC thumbnail stream (already implemented) | Unknown |
| Application volume | WASAPI audio session by PID through `AudioMixerService.SetSession` | **Feasible regardless of SMTC.** Works if Apple Music holds an audio session. Already used by the mixer page. |
| MusicKit | Official Apple SDK | **Not relevant.** MusicKit plays Apple Music inside your own app or web player and does not control the desktop Apple Music app [docs]. It needs a developer token and a user token, and would make KZ a separate player. Do not use. |
| Legacy iTunes COM (`iTunes.Application`) | Old automation interface | Unknown for the new Store app. Do not depend on it. |
| Spotify-style rich API (playlists, like, library) | none | **Not available.** Apple Music exposes only what SMTC offers. |

**Probe plan (Phase 3, about 10 minutes, user-assisted):** with Apple Music open and a track playing, read `GET /api/v1/Media/sessions` (already exists) and record the session `Id`, the `Can*` flags, the title and artist, the thumbnail and the position. Then call `POST /api/v1/Media/sessions/{id}/command` with `pause` and `play`. The result decides the exact feature set. No new code is required for the probe.

**Expected design if SMTC works:** Apple Music becomes a *media session adapter* (not an integration with credentials). It is matched to its running process by AUMID or process name, and shows the standard media card. Mixer volume attaches by PID.

---

## 4. Running Apps — technical design

### 4.1 What exists [code]
- `GET /api/system/apps` → `ForegroundWatcher.RunningApps()` (`Services/ForegroundWatcher.cs:122`): `EnumWindows` + `IsWindowVisible` + non-empty title. It dedupes by PID (`TryAdd`) and then by process name (`GroupBy`), takes the first title, and skips `explorer`, `TextInputHost` and `ApplicationFrameHost`. It returns `FieldOption(value=processName, label="name — title")`, a shape designed for an editor dropdown.
- `AppActivator.ActivateWindow` uses `FindWindow` by exact title and `SetForegroundWindow` (no restore, no minimize). `SoftwareController.activate` is disabled by `EnableSoftwareActivation=false` and also sends keys, so **do not reuse it**.
- Audio: `AudioMixerService` already knows PID, executable path (`QueryProcessPath`), display name, icon and `GroupKey` for each audio session (`AudioSessionDto`, `Services/AudioMixerService.cs:4`). Process icons come from `AppLibrary.ExtractIconPng`.
- `ForegroundWatcher` polls the foreground window every 500 ms, only when a phone is connected, only for profile auto-switching.

### 4.2 Gaps
No PID, HWND, path or icon. No distinction between a window and a process (the DTO is one row per process name). No focus, restore, minimize or close. No tray or background-app handling. UWP windows appear as `ApplicationFrameHost` and are dropped. The audio association exists but is not linked.

### 4.3 Proposed `RunningAppsService` (new, `DroidDeck/Services/RunningAppsService.cs`)

**Model (one row per application, not per process):**

```
RunningApp {
  id            // stable key: "<exePath lowercase>" or AUMID for packaged apps
  name          // FileDescription / ProductName, fallback process name
  processName, exePath, aumid?
  pids[]        // all processes in the group
  mainWindow { hwnd, title, pid, minimized, foreground }
  windows[]?    // optional, only on detail request
  icon          // cached PNG (hash-keyed, reused from AppLibrary icon cache)
  audio?        // { sessionIds[], volume, muted, active } from AudioMixerService
  integration?  // { key: "discord", capabilities: [...] }, see §5
  isForeground
}
```

**Grouping rules** (multi-process apps):
1. Key by **resolved executable path**, not by process name or PID. Discord's `Update.exe` and versioned folders (`app-1.0.xxxx`) are normalized to the stable name through `FileVersionInfo.ProductName`.
2. Firefox, Chrome and Steam spawn many child processes with one window process. Only processes that own a **top-level visible window** define the row. Child processes join the group through the same exe path, so `pids[]` is complete and the audio sessions of all children are merged.
3. For the **main window**, choose the visible, titled, non-tool window (`WS_EX_TOOLWINDOW` not set, no owner) that has the largest area, or the foreground one if it belongs to the group.
4. **Packaged (UWP/WinUI) apps:** `ApplicationFrameHost` hosts the visible frame. Resolve the real process with the child window of class `Windows.UI.Core.CoreWindow`, or use `GetApplicationUserModelId`. Without this, Apple Music, Spotify (Store), Calculator and similar apps vanish or are listed as `ApplicationFrameHost`.
5. **Relevance filter** (the "relevant applications" requirement): show an app if it has a visible window **and** (it is in the user's library, **or** it has an audio session, **or** it matches an integration). Hide shell, input hosts, the KZ process itself and cloaked windows (`DWMWA_CLOAKED`, which removes suspended UWP ghosts and other virtual-desktop windows). A "show all" switch returns everything.

**Operations (all by `id`, never by client-supplied path or HWND):**

| Operation | API | Behavior |
|---|---|---|
| Focus | `POST /api/running-apps/{id}/focus` | `ShowWindowAsync(SW_RESTORE)` if minimized, then `SetForegroundWindow`. A background process often cannot steal focus, so use the `AllowSetForegroundWindow` and Alt-key workaround only if testing shows it is needed. If Windows still refuses, return `409 focus_denied`. |
| Minimize | `POST /api/running-apps/{id}/minimize` | `ShowWindowAsync(SW_MINIMIZE)`. |
| Close | `POST /api/running-apps/{id}/close` | **Graceful only:** `PostMessage(WM_CLOSE)` to the main window, then `Process.CloseMainWindow` as a fallback. **No** `Kill`, no force. Return `{ closed: false, reason }` if the app is still alive after about 3 s. The phone shows a confirm dialog. |

**Endpoints:**
- `GET /api/running-apps?all=false` → list (icons are separate to keep the payload light: `GET /api/running-apps/{id}/icon`, cached with ETag).
- Keep `GET /api/system/apps` unchanged for the existing editor.
- **SignalR:** `ReceiveRunningApps` (diff only, debounced).

**Change detection without a hot loop:**
- Use `SetWinEventHook` (`EVENT_OBJECT_CREATE/DESTROY/SHOW/HIDE`, `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_NAMECHANGE`) on a dedicated message-pump thread. This is event-driven, with zero cost when idle. `ForegroundWatcher`'s comment explains why it uses 500 ms polling instead (a pump thread is needed), and that trade-off is acceptable here because the thread is small and shared.
- Fallback if hooks are rejected in review: poll every 2 s **only while a phone is connected and on the Apps screen**, using a client "subscribe" call (`DeckHub.SubscribeRunningApps/Unsubscribe`).
- Run enumeration only when subscribed. Cache path, name and icon per exe path.

**Safety (anti-cheat):** use only `EnumWindows`, `GetWindowText`, `QueryFullProcessImageName` (`PROCESS_QUERY_LIMITED_INFORMATION`) and window messages. No `OpenProcess` with read or write memory access, no DLL injection, no hooks into other processes (`SetWinEventHook` with `WINEVENT_OUTOFCONTEXT` does not inject). Do not enumerate modules. Consider a deny-list so games with anti-cheat (EAC, BattlEye, Vanguard) are listed read-only: no close, no focus. This was already the stance of the Steam and Battle.net launch code, which launches through the launcher and not the exe.

**Cost:** small. About 400 lines of C# plus P/Invoke, reusing `AppLibrary.ExtractIconPng` and `AudioMixerService` data.

---

## 5. Integration architecture

### 5.1 Capability model

Add a thin layer on top of `IIntegration` without rewriting existing classes:

```csharp
public enum Capability { Playback, AudioControl, VoiceControl, SceneManagement,
                         StreamRecord, SmartDevice, StateMonitoring, MediaSession }

public interface ICapableIntegration : IIntegration
{
    IReadOnlySet<Capability> Capabilities { get; }
    SetupInfo Setup { get; }            // fields[], needsPcSetup, docsUrl, requirements text
    IntegrationState State { get; }     // NotConfigured | Configuring | Connecting | Connected | Error(msg)
    bool WantsConnection { get; }       // see §5.3
    // Contextual controls: declarative, rendered by the app
    IReadOnlyList<ControlDescriptor> Controls { get; }
    Task InvokeAsync(string control, JsonElement args, CancellationToken ct);
}
```

- `ControlDescriptor` is a tiny schema (`id`, `label`, `icon`, `kind: button|toggle|slider|picker`, `stateKey`). The app renders any integration without per-integration Dart code. This reuses the existing `ActionSchema`/`ActionField` idea in `Actions/ActionSchema.cs`.
- Adapters wrap the current services. `ObsService`, `DiscordRpcService` and `SpotifyIntegration` implement the interface without changing their internals.
- **Media sessions** (Apple Music, Spotify desktop, browsers) are not "integrations" but a **provider type**: `MediaSession` capability from SMTC, with no credentials.

| Integration | Capabilities |
|---|---|
| Discord | VoiceControl, AudioControl, StateMonitoring |
| Spotify (Web API) | Playback, StateMonitoring |
| Apple Music (SMTC) | MediaSession, Playback (subset), AudioControl (via mixer) |
| OBS | SceneManagement, StreamRecord, AudioControl, StateMonitoring |
| Home Assistant | SmartDevice, StateMonitoring |
| Hue, Govee, MQTT | SmartDevice |
| Twitch, YouTube | StreamRecord (metadata), StateMonitoring |

### 5.2 API (all inside the existing authenticated surface)

| Endpoint | Purpose |
|---|---|
| `GET /api/integrations` (extend) | Adds `capabilities[]`, `state`, `configured`, `requirements[]`. Backward compatible. |
| `GET /api/integrations/{name}/controls` | Control descriptors and current values for the contextual panel. |
| `POST /api/integrations/{name}/invoke` | `{control, args}`. The server validates and maps to the real call. |
| Existing `POST /api/integrations/{name}` | Phone-side configuration of non-secret fields only (see 5.4). |
| `GET /api/running-apps…` | §4. |
| SignalR | One consolidated `ReceiveIntegrationState(name, state)` plus the existing per-integration events kept for compatibility. |

The pairing model is untouched: same API key, same SignalR connection (`/deckHub`), same `?access_token=`.

### 5.3 Connect and poll only when needed

Rules, implemented in a small `IntegrationScheduler` (or an `AccountIntegration` base change):

1. **Not configured → no task, no timer, no socket.** Replace the always-on `Wait(2–3 s)` loops with a one-shot wait on a `SemaphoreSlim`/`TaskCompletionSource` that `ConfigureAsync` releases.
2. **Configured → connect lazily** when either (a) a client is connected (`DeckHub.ConnectedCount > 0`) and the integration is relevant to the current screen, or (b) it has state that must be kept (OBS recording state is only required if a client asks).
3. **Poll only on demand** for REST-only services (Spotify, Twitch, YouTube): a client "watch" subscription (`WatchIntegration(name)` / `UnwatchIntegration`) starts the poll and ends it when nobody watches. That replaces the button gate (F1). Back off on 429 and use `Retry-After`.
4. **Local socket integrations** (OBS, Discord): connect when the target process is running (Running Apps service tells us), instead of retrying every 10 s blindly. When the process exits, close the pipe or socket.
5. **Push over poll** wherever the service supports it (OBS events, Discord subscriptions, HA `state_changed`).

### 5.4 Credentials and secrets

- Keep `SecretFile` (DPAPI, current user). It is adequate for a single-user desktop tool. Its limit: any process running as the same user can decrypt it. Document that. Do not log tokens (the Radio integration already avoids URL logging).
- **Secrets are write-only from the phone:** responses never include secrets (already true in code), and new rule: *client secrets, tokens and passwords are entered on the PC*, in the existing local configurator (`/api/pairing/local-key` flow, `kz_config_shell.dart`) or through a new PC-side dialog. The phone only gets status and non-secret fields (host, port, Client ID). This resolves F2 for secrets without removing phone-side setup for simple integrations.
- Gate `POST/DELETE /api/integrations/{name}` and the legacy `config` routes with `ActionPolicy.IsLocal` for **credential fields**, or at least for any field that makes the PC call out to a user-supplied address (HA URL, MQTT server, OBS host). Reject public IPs for those fields (allow loopback, RFC1918 and `.local`).
- OAuth callbacks stay `[AllowAnonymous]` with one-shot `state` + PKCE (already so).

### 5.5 Reuse of what exists

| Reuse as is | Change | New |
|---|---|---|
| `SecretFile`, `AccountIntegration` store | `ActionsOfType` gating → watch subscriptions | `ICapableIntegration`, `ControlDescriptor` |
| `OAuthTokens`, `ApiCall` | Spotify like endpoint | `IntegrationScheduler` / watch hub methods |
| `ObsService`, `DiscordRpcService` | Add REST invoke routes | `RunningAppsService` |
| `MediaControlService`, `AudioMixerService` | Link process ↔ session ↔ integration | Media-session adapter for Apple Music |
| `DeckHub` replay of provider state | Replay integration state | — |

---

## 6. User experience

The Dashboard, Favorites (Apps), Audio Mixer, Hardware and Appearance stay as they are. The legacy Buttons deck is not restored.

### 6.1 Navigation
Current rail: Dashboard · Apps · Hardware · Controls · Settings (`kz_shell.dart:29`). **No new top-level destination in the default layout.** Proposed placement keeps the rail at five items:

- **Running Apps:** a collapsible "Running now" strip or section **inside the existing Apps page** (above Favorites, hidden when empty), plus the full list behind a "More" button. Tapping an app opens a small bottom sheet with Focus / Minimize / Close, its mixer slider (reusing `kz_audio_widgets.dart`), and its contextual controls. This is opt-in through a Settings switch (default off), so the Dashboard is unchanged by default.
- **Integrations catalog:** Settings → Integrations (already exists, Discord only). Becomes a list of cards (icon, name, state chip: *Not set up / Connecting / Connected / Needs attention*, one-line requirement). The detail page shows setup steps, status, the capability list and Disconnect.
- **Contextual controls:** rendered by the app from `ControlDescriptor`s inside the app sheet (Running Apps) and optionally as one compact card on the Dashboard **only if the user pins it** (default: not shown). Examples: Discord (mute, deafen, voice-channel name), Spotify (cover, title, play/pause/next, like), Apple Music (media card from SMTC with only the buttons whose `Can*` flag is true), OBS (scene list, REC and LIVE toggles with state), Home Assistant (chosen devices).
- Controls appear **only when the integration is connected and its app is running** (Discord, OBS) or has an active session (Spotify, Apple Music).

### 6.2 Huawei Mate 20 Lite landscape (2244×1080, ~6.3")
- Reuse the landscape rail layout and `KzCard` building blocks. No new layout system.
- Lists are lazy (`ListView.builder`). Icons are fetched on demand and cached, 48 px thumbnails, no full-size artwork except the active media cover (the backend already shrinks it with `InfoService.Shrink`).
- Bottom sheets are limited to the screen's short edge (~1080 px high). Keep tap targets at 48 dp or more.
- Subscribe to `RunningApps` and watch streams **only while the relevant screen is visible**. Unsubscribe in `dispose`, to avoid battery and Wi-Fi use.
- Payload budget: list rows are about 300 bytes without icons.

---

## 7. Roadmap

Each phase is independently shippable, ends with a live test on real hardware, and requires approval before the next. Estimates are for focused work, as a rough relative size, not a promise.

| Phase | Scope | Size | Depends on | Shared infra |
|---|---|---|---|---|
| **2. Running Apps** | `RunningAppsService` (grouping, icon, audio link), focus, minimize, graceful close, `GET /api/running-apps`, SignalR diff, watch subscription, Apps-page section behind a setting | M | none | Provides process → app identity for every later phase |
| **3. Capability layer + Apple Music probe** | `ICapableIntegration`, `GET …/controls`, `invoke`, `WatchIntegration`, scheduler rule §5.3, catalog UI skeleton. Live SMTC probe of Apple Music. Media-session adapter if probe passes | M | Phase 2 | Foundation for 4–7 |
| **4. Discord** | `IsLocal` gate and PC-side credential entry, controls (mute, deafen, channel name), tester and approval guidance text, remove soundboard from exposure, process-aware connect | S–M | Phase 3 | Capability layer, Running Apps (connect only when Discord runs) |
| **5. Spotify** | `/me/library` fix, drop the button gate, app-side `ReceiveSpotifyState`, media card, 127.0.0.1 redirect check, Premium and Dev Mode notes | M | Phase 3 | Same media-card UI as Apple Music |
| **6. Apple Music** (UI part) | Contextual card, per-app volume. Capabilities limited to what the probe shows | S | Phases 2–3 | Media card from Phase 5 |
| **7. OBS** | Invoke routes and descriptors (scenes, record, stream), connect-when-running, auth-error message | S–M | Phases 3, 2 | Capability layer |
| **8. Home Assistant, then MQTT, Govee, Hue** | User-chosen device list in KZ, `SmartDevice` controls. Verify Hue v1 status first | M each | Phase 3 | `SmartDevice` descriptors |
| **Later / parked** | Twitch, YouTube, Voicemeeter, Elgato, Streamer.bot, VTube Studio | — | — | — |
| **Not planned** | Companion, vMix, mAirList, RadioDJ, Soundboard (MyInstants), Tuya (until own registration) | — | — | — |

Ordering note: Apple Music is reordered slightly. Its **probe** runs in Phase 3 (cheap, and it decides the design of the media card that Spotify also uses), and the UI part follows Spotify, in the position you asked for.

**Shared infrastructure summary**
- Running Apps → Discord, OBS (process-aware connect), Apple Music (AUMID, volume), the mixer.
- Capability layer and watch subscriptions → everything.
- Media card → Spotify and Apple Music.
- `SmartDevice` descriptors and device-list store → Home Assistant, MQTT, Govee, Hue.

**Avoid:** a new dependency in this plan. Everything uses existing packages (`NAudio`, WinRT `Windows.Media.Control`, MQTTnet, built-in `ClientWebSocket`). No large refactor: existing services are wrapped, not rewritten.

---

## 8. Risks, dependencies and required user configuration

### 8.1 Required user configuration

| Integration | What the user must do |
|---|---|
| Discord | Create an app at the Discord Developer Portal; add redirect `http://localhost:4787/discord`; **add their own account as an app tester** (RPC is otherwise unapproved), then approve the consent prompt in the Discord client. Keep Discord desktop running. |
| Spotify | Create an app (Dev Mode requires **Premium** on the owner account); register redirect `http://127.0.0.1:4787/api/integrations/spotify/callback`; paste the Client ID; log in through the browser. Up to 5 authorized users. |
| Apple Music | Install the Microsoft Store app. Nothing else, subject to the probe. |
| OBS | Enable WebSocket Server (Tools → WebSocket Server Settings), note port and password. |
| Home Assistant | Create a long-lived access token in the user profile; give the HA URL. |
| Hue | Press the bridge link button during pairing. |
| Govee | Turn on LAN Control per device in Govee Home. |
| MQTT | Broker address and credentials. |
| Twitch, YouTube | Create their own app or client. |

### 8.2 Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Discord RPC remains restricted or changes (Discord recommends the Social SDK) | Medium | Discord voice controls unavailable to non-testers | Tester-list setup text, clear "unavailable" state, keep voice features optional. Do not use unofficial or self-bot methods. |
| Spotify Dev Mode limits (Premium owner, 5 users) | High (already live) | Cannot ship as a public turnkey login | Position as bring-your-own-app. State this in the catalog. |
| Apple Music exposes little or nothing through SMTC | Unknown | Feature reduced to detect and volume | Probe first (Phase 3). Define the feature by the result. |
| Multi-process grouping errors (Firefox, Steam, Discord, UWP) | Medium | Duplicate or missing rows | Group by exe path and window owner. Test against the named apps. Provide "show all". |
| Focus-steal restrictions | Medium | Focus sometimes ignored | Return an explicit `focus_denied`. Do not hack input. |
| Anti-cheat interaction | Low (no injection) | Account risk | Only the safe APIs in §4.3, plus a read-only list for protected games. |
| Config endpoints reachable by any paired phone (F2) | Medium | Repointing or credential overwrite | `IsLocal` on credential and address fields. |
| Shared API key over HTTP on LAN (F5) | Existing | Interception on a hostile LAN | Out of scope here. Recommend a later phase for per-device keys and TLS. |
| Hue v1 retirement (unverified) | Unknown | Hue needs a v2 rewrite | Verify before Phase 8. |
| Tuya registration borrowed from HA | High | Pairing can break at any time | Keep it parked. |
| Unverified integrations in this audit | n/a | Wrong "ready" assumptions | Every phase begins with a live test. |

### 8.3 Dependencies

- Windows 10 1809+ (`net8.0-windows10.0.17763.0`, `DroidDeck.csproj:5`). `GlobalSystemMediaTransportControls` needs no extra package.
- WebView2 and `LibreHardwareMonitor` are unaffected.
- No new NuGet or pub packages are needed.

---

## 9. Open questions for you (answers needed before Phase 2/4)

1. **Running Apps visibility:** inside the Apps page (proposed), or its own rail item? Default: off or on?
2. **Where secrets are entered:** PC-only (proposed), or keep the phone form for Discord's client secret and accept the risk?
3. **Discord tester requirement:** are you comfortable documenting "add yourself as an app tester" as the supported path?
4. **Close action:** want a confirm dialog on every graceful close (proposed)?
5. **Apple Music probe:** may I ask you to open Apple Music and play a track once, so the probe can run in Phase 3? (I did not launch it.)

## 10. Source references (index)

- Registry and base: `DroidDeck/ServiceRegistration.cs:86`, `DroidDeck/Integrations/IIntegration.cs`, `AccountIntegration.cs`, `IntegrationsController.cs`
- Discord: `Integrations/Discord/DiscordRpcService.cs`, `DiscordController.cs`, `DiscordAutoConnect.cs`; app: `app/lib/src/kz/kz_integrations_page.dart`
- OBS: `Integrations/Obs/ObsService.cs`, `ObsController.cs`, `ObsAutoConnect.cs`
- Spotify: `Integrations/Spotify/SpotifyIntegration.cs`, `SpotifyProvider.cs`
- Twitch / YouTube: `Integrations/Twitch/*`, `Integrations/YouTube/*`
- Home Assistant: `Integrations/HomeAssistant/HomeAssistantIntegration.cs`
- Hue / Govee / Tuya / MQTT: `Integrations/Hue/*`, `Govee/*`, `Tuya/*`, `Mqtt/*`; `docs/tuya-business-review.md`
- Media: `Services/MediaControlService.cs`, `Controllers/MediaController.cs`
- Audio: `Services/AudioMixerService.cs`, `Controllers/AudioController.cs`
- Apps: `Controllers/LibraryController.cs:28`, `Services/ForegroundWatcher.cs`, `Services/AppActivator.cs`, `Controllers/AppsController.cs`
- Security: `Auth/*`, `Lib/SecretFile.cs`, `Services/ActionPolicy.cs`, `Program.cs:105,255,297,366`
- Hub and app: `Hubs/DeckHub.cs`, `app/lib/src/services/signalr_service.dart`, `app/lib/src/kz/kz_shell.dart`

**Official documentation consulted (2026-11-21)**
- Discord RPC: https://docs.discord.com/developers/topics/rpc and OAuth2: https://docs.discord.com/developers/topics/oauth2
- Spotify Feb 2026 migration guide: https://developer.spotify.com/documentation/web-api/tutorials/february-2026-migration-guide, changelog: https://developer.spotify.com/documentation/web-api/references/changes/february-2026, quota modes: https://developer.spotify.com/documentation/web-api/concepts/quota-modes, redirect URIs: https://developer.spotify.com/documentation/web-api/concepts/redirect_uri
- obs-websocket protocol: https://github.com/obsproject/obs-websocket/blob/release/5.0.0/docs/generated/protocol.md
- Twitch authentication: https://dev.twitch.tv/docs/authentication/
- Windows media control: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession
- MusicKit: https://developer.apple.com/musickit/
