# Phase 2: Running Apps

Design from `docs/integration-audit-phase1.md` §4, as implemented. Section "Running now" on the Android Apps page, above Favorites. On by default; Appearance → Sections → "Running apps (Apps screen)" turns it off.

## What it does

- Lists applications that have a real top-level window: visible, titled, not owned, not a tool window, not cloaked (other virtual desktops and suspended Store apps), not zero-sized. Shell and system hosts are filtered out (`RunningAppsGrouper.IsUserFacing`).
- One row per application, not per process or window. Identity is the hash of the owning executable path, the same value the audio mixer uses as `groupKey` (`AudioMixerService.ShortHash`). That is how the volume and mute sliders attach to the right app. Names are never used to match.
- Multi-process apps:
  - Several processes or windows from one `.exe` are one row with a window list.
  - A window owned by a helper process rolls up to its parent when the helper lives inside the parent's install folder (Steam: `steamwebhelper.exe` → `steam.exe`). The parent must not be under `%WINDIR%` or at a drive root, so programs started from Explorer never become "Windows Explorer" (`RunningAppsGrouper.IsOwnerOf`).
  - Store apps are hosted in `ApplicationFrameHost`; the real process is found through the child CoreWindow.
- Actions (all by app id and window id, validated against a fresh enumeration):
  - **Focus / Restore**: `ShowWindowAsync(SW_RESTORE)` if minimized, then `SetForegroundWindow`, then `SwitchToThisWindow`. No `AttachThreadInput` and no simulated keys. Returns `409 focus_denied` if Windows refuses.
  - **Minimize**: `SW_MINIMIZE`.
  - **Close**: `WM_CLOSE` only, like the X button. Requires `confirm=true` (the phone asks first). Never terminates a process. If the app keeps its window (for example "save changes?"), the answer is `closed:false`.
- Elevation: apps running as administrator are shown locked. Every action returns `409 elevated` instead of trying to cross the integrity boundary.

## API (authenticated like everything else)

| Route | Purpose |
|---|---|
| `GET /api/running-apps` | `{ apps: [{ id, name, processName, title, foreground, minimized, elevated, windowCount, windows[] }] }`. No paths. |
| `GET /api/running-apps/{id}/icon` | PNG, cached by the client |
| `POST /api/running-apps/{id}/focus?windowId=` | 200, 404 `app_gone`/`window_gone`, 409 `focus_denied`/`elevated` |
| `POST /api/running-apps/{id}/minimize?windowId=` | same |
| `POST /api/running-apps/{id}/close?windowId=&confirm=true` | 400 `confirmation_required` without `confirm` |
| Hub `WatchRunningApps` / `UnwatchRunningApps` | Start or stop the push `ReceiveRunningApps` (full list, only when it changed) |

`GET /api/system/apps` is unchanged (the editor still uses it).

## Idle cost

Nothing runs until a phone calls `WatchRunningApps`, which happens only while the Apps page is visible and the app is in the foreground. Then one small thread runs a message loop with out-of-context `SetWinEventHook` hooks (foreground, minimize, create/show/hide/destroy, title, cloak). Events are debounced for 500 ms, and a snapshot is sent only if the list changed. A 10 s heartbeat covers events from elevated windows, which a lower-integrity hook does not receive. When the last watcher leaves or disconnects, the hooks and thread are removed.

## Known limits

- Windows on other virtual desktops are hidden (cloaked).
- Apps that hide to the tray on close lose their row; the process is still running.
- Elevated apps can be listed but not controlled from a non-elevated KZ Companion.
- Focus can be refused by Windows' foreground rules. The phone shows a message.
- The audio association needs the app to hold an audio session under the same `.exe`. An app whose audio plays from a different executable folder than its window process has a row but no slider.
