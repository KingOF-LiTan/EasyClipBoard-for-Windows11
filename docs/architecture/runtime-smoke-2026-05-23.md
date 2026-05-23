# Runtime Smoke 2026-05-23

## Environment

- Build command: `powershell -ExecutionPolicy Bypass -File scripts/verify-build.ps1`
- Built executable used for smoke: `clip/clip/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/clip.exe`
- Existing process observed before smoke: `clip.exe` from `win-x64/AppX/clip.exe`

## Automated Checks

| Check | Result | Notes |
|---|---|---|
| Local compile verification | Pass | `scripts/verify-build.ps1` produced `clip.dll`; warnings only at `MainWindow.xaml.cs:130` nullability. |
| Frontend syntax | Pass | `scripts/check-frontend-js.ps1` ran `node --check` for `app.js` and all `wwwroot/js/*.js`. |
| Static WebView page load | Pass | Playwright opened `wwwroot/index.html` and found `#content-layer`. |
| Inline handler retirement | Pass | No `onclick`, `onchange`, `oninput`, or `.onclick` remain in `index.html` or `wwwroot/js/*.js`. |
| Clipboard capture smoke | False negative / resolved | Launched `clip.exe`, wrote a unique marker through `Set-Clipboard`, initially the DB was not found. Root cause: stale `AppX/clip.exe` process interfered with the test. After process isolation, storage initialization and clipboard capture verified working — marker text found in WAL. See Debug D01 resolution below. |
| Runtime smoke harness | Pass | `scripts/check-runtime-smoke.ps1` verifies process cleanup, expected executable path, single-instance guard, storage DB creation, and clipboard marker capture in DB/WAL. |

## Manual Checks Still Needed

These require a visible desktop session with the app known to have initialized storage:

- Main hotkey toggles UI at cursor.
- Text/image/file copies appear in history.
- Image thumbnail lazy-loads and native preview opens.
- Single click selects; double click or explicit paste button pastes.
- Enter, Space, and 1-9 shortcuts work.
- Drag starts native drag and does not paste.
- Favorite, delete, tag, and clear history work.
- Vault add/search/copy username/copy password/delete work.
- Language, theme, background image, clear background, mask opacity, autostart, and shortcut recorder work.
- Ctrl+Shift+V plain-text paste works in an external target app.

## Follow-Up

- The storage initialization blocker is resolved as a smoke harness false negative.
- Keep `scripts/check-runtime-smoke.ps1` in the verification path to prevent stale `clip.exe` processes from invalidating future runtime checks.
- Continue with interaction experience hardening after the runtime smoke remains green.

---

## Debug D01: Clipboard Capture Smoke — Resolution (2026-05-23 01:15)

### Root Cause

**False negative from stale process interference.** The clipboard capture pipeline (storage init, clipboard listener, clipboard reader, save to SQLite) is fully functional. The smoke test failed because:

1. A stale `AppX/clip.exe` process (PID 49044, started 2026-05-22) was already running before the test.
2. The smoke test launched `win-x64/clip.exe` alongside the existing `AppX/clip.exe`.
3. With two instances running, either the storage initialization was interfered with, or the smoke harness checked the wrong process or path.

### Evidence (Decision Table Applied)

Applying the decision table from `D01.6`:

| Evidence | Root Cause Layer | Status |
|---|---|---|
| No intended `win-x64\clip.exe` process | Smoke harness/process isolation | **TRUE (initial state)** |
| `OnLaunched()` logs absent | App startup/WinAppSDK launch | Not needed — process isolation fixed upstream |
| `StorageService` constructor logs absent | Startup path before storage | Not needed — process isolation fixed upstream |
| Constructor logs path but directory/DB absent | Storage path/permissions | Not needed |
| `InitAsync()` starts but does not complete | Storage migration/open | Not needed |

**Selected root cause: Smoke harness / process isolation.**

After isolation (kill stale process → launch correct exe):
- `data.db` created at `2026-05-23 01:14:53` (instant after launch)
- `SMOKE_MARKER_20260523_011535` captured at `2026-05-23 01:15:35` and found in WAL file
- Entire pipeline verified: `WM_CLIPBOARDUPDATE` → `ClipboardReader.ReadAsync()` → `SaveItemAsync()` → WAL

### Verification Steps

1. Killed stale `AppX/clip.exe` (PID 49044)
2. Backed up existing `data.db` (from stale process, created 2026-05-23 00:46:51)
3. Launched `win-x64/clip.exe` → PID 37564, path verified as correct
4. Dir and DB existed immediately after launch
5. Wrote `SMOKE_MARKER_20260523_011535` via `Set-Clipboard`
6. Searched copied WAL → marker text found with SQLite framing

### Recommended Fix

1. **Smoke harness**: Add a pre-check to kill any existing `clip.exe` processes before launching the test instance. Completed in `scripts/check-runtime-smoke.ps1`.
2. **Smoke harness**: Wait after process launch for WinUI startup + storage init before writing the clipboard marker. Completed in `scripts/check-runtime-smoke.ps1`.
3. **Runtime guard**: Add a single-instance check in `App.OnLaunched()` to prevent duplicate processes from running simultaneously. Completed with a per-user named mutex.
