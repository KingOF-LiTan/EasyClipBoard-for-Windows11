# Debug Task Card D01: Clipboard Capture Does Not Create Storage DB

> Role: Debugger
>
> Goal: find the root cause for the runtime smoke blocker where launching `clip.exe`, writing a clipboard marker, and checking `%LocalAppData%/WinClipboard/data.db` does not produce the expected database.
>
> Rule: do not patch behavior until the failing layer is identified with evidence.

## Current Evidence

- `scripts/verify-build.ps1` builds the project successfully with MSIX tooling disabled.
- `scripts/check-frontend-js.ps1` passes.
- Inline handler cleanup is verified.
- `docs/architecture/runtime-smoke-2026-05-23.md` records `Clipboard capture smoke` as `Fail / blocked`.
- The smoke launched `clip/clip/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/clip.exe`.
- An existing `clip.exe` from `win-x64/AppX/clip.exe` was observed before smoke.
- After writing a unique marker through `Set-Clipboard`, `%LocalAppData%/WinClipboard/data.db` was not created.

## Relevant Code Paths

- `clip/clip/App.xaml.cs`
  - `OnLaunched()` creates `_storage = new StorageService()`.
  - `OnLaunched()` awaits `_storage.InitAsync()`.
  - `OnLaunched()` sets `_isInitialized = true` after storage init.
  - `OnLaunched()` wires `_host.ClipboardChanged += async () => await OnClipboardChangedAsync()`.
  - `OnClipboardChangedAsync()` reads clipboard and calls `_storage.SaveItemAsync(finalDraft)`.

- `clip/clip/Core/Storage/StorageService.cs`
  - Constructor resolves base dir with `Environment.SpecialFolder.LocalApplicationData`.
  - Constructor creates `%LocalAppData%/WinClipboard`.
  - Constructor sets `_dbPath = <baseDir>/data.db`.
  - `InitAsync()` opens `StorageConnection` and runs migration.

- `clip/clip/Native/MessageOnlyWindowHost.cs`
  - `Start()` creates the message thread.
  - `ThreadMain()` creates message-only HWND.
  - `ThreadMain()` calls `_clipboard.Attach(_hwnd)`.

- `clip/clip/Native/ClipboardListenerService.cs`
  - `Attach()` calls `Win32Helper.AddClipboardFormatListener(hwnd)`.
  - `HandleMessage()` maps `WM_CLIPBOARDUPDATE` to `ClipboardChanged`.

## Debug Questions

Answer these in order. Do not skip ahead.

1. Which `clip.exe` process is actually running during smoke?
2. Does `App.OnLaunched()` execute in that process?
3. Does `StorageService` constructor run, and what exact `DbPath` does it compute?
4. Does `StorageService.InitAsync()` complete or throw?
5. If storage initializes, does `MessageOnlyWindowHost.ThreadMain()` create a nonzero HWND?
6. Does `AddClipboardFormatListener()` return success?
7. Does `WM_CLIPBOARDUPDATE` reach `ClipboardListenerService.HandleMessage()`?
8. Does `OnClipboardChangedAsync()` run and pass `_isInitialized && _storage != null`?
9. Does `ClipboardReader.ReadAsync()` return a text draft for the marker?
10. Does `SaveItemAsync()` run, return an id, and create/update `data.db`?

## Task D01.1: Reproduce With Process Isolation

**Goal:** Make the failure reproducible without a stale AppX process confusing the result.

**Commands:**

```powershell
Get-Process clip -ErrorAction SilentlyContinue | Select-Object Id,Path,StartTime
```

Then stop only confirmed test processes if needed:

```powershell
Get-Process clip -ErrorAction SilentlyContinue | Stop-Process
```

Start the intended executable:

```powershell
$exe = Resolve-Path "clip\clip\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\clip.exe"
Start-Process -FilePath $exe
Start-Sleep -Seconds 3
Get-Process clip -ErrorAction SilentlyContinue | Select-Object Id,Path,StartTime
```

**Expected evidence:**

- Exactly one `clip.exe` process.
- `Path` matches `win-x64\clip.exe`, not `win-x64\AppX\clip.exe`.

**If not true:**

- Stop and document which executable is running.
- Do not continue clipboard checks until process identity is controlled.

## Task D01.2: Verify Storage Path Creation Immediately After Launch

**Goal:** Determine whether startup storage initialization runs before any clipboard event.

**Commands:**

```powershell
$db = Join-Path $env:LOCALAPPDATA "WinClipboard\data.db"
$dir = Split-Path $db
Test-Path $dir
Test-Path $db
Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue
```

**Expected evidence:**

- Directory exists after app launch.
- `data.db` exists after `StorageService.InitAsync()`, even before clipboard capture.

**Interpretation:**

- If directory and DB do not exist, failure is before or inside `StorageService.InitAsync()`.
- If DB exists before clipboard marker, storage startup is not the root cause; continue to listener/capture.

## Task D01.3: Add Temporary Startup Diagnostics

**Goal:** Capture exact startup/storage state if DB is not created.

**Affected files:**

- Temporarily modify: `clip/clip/App.xaml.cs`
- Temporarily modify: `clip/clip/Core/Storage/StorageService.cs`

**Instrumentation points:**

- At start of `App.OnLaunched()`: log process path and current process id.
- Immediately before `new StorageService()`.
- Immediately after `new StorageService()`: log `_storage.DbPath`.
- Immediately before and after `await _storage.InitAsync()`.
- In `StorageService` constructor: log resolved base dir, `_dbPath`, and blob dir.
- In `StorageService.InitAsync()`: log before `_conn.OpenAsync()` and after migration.

**Example log strings:**

```csharp
System.Diagnostics.Debug.WriteLine($"[StartupProbe] exe={Environment.ProcessPath} pid={Environment.ProcessId}");
System.Diagnostics.Debug.WriteLine($"[StartupProbe] storage db={_storage.DbPath}");
System.Diagnostics.Debug.WriteLine("[StartupProbe] storage init begin");
System.Diagnostics.Debug.WriteLine("[StartupProbe] storage init complete");
```

**Verification:**

- Rebuild with `scripts/verify-build.ps1`.
- Relaunch isolated process.
- Capture Debug output through the available local debugger/log mechanism.

**Important:**

- These diagnostics are temporary unless converted into a deliberate diagnostics feature.
- Do not leave noisy startup probes in final production code without architect approval.

## Task D01.4: Add Clipboard Listener Diagnostics

**Goal:** Determine whether the native clipboard listener is registered and receiving messages.

**Affected files:**

- Temporarily modify: `clip/clip/Native/MessageOnlyWindowHost.cs`
- Temporarily modify: `clip/clip/Native/ClipboardListenerService.cs`
- Temporarily modify: `clip/clip/App.xaml.cs`

**Instrumentation points:**

- After `CreateMessageOnlyWindow()`: log `_hwnd`.
- After `_clipboard.Attach(_hwnd)`: log attach attempted.
- In `ClipboardListenerService.Attach()`: log return value from `AddClipboardFormatListener` if helper exposes it. If helper currently returns `void`, inspect `Win32Helper.AddClipboardFormatListener` before changing signature.
- In `ClipboardListenerService.HandleMessage()`: log when `WM_CLIPBOARDUPDATE` is seen.
- At top of `OnClipboardChangedAsync()`: log `_isInitialized`, `_storage != null`, and suppress state.

**Expected evidence:**

- Nonzero HWND.
- Clipboard listener attach happens once.
- `WM_CLIPBOARDUPDATE` fires after `Set-Clipboard`.
- `OnClipboardChangedAsync()` runs.

## Task D01.5: Add Clipboard Read And Save Diagnostics

**Goal:** Determine whether the marker is read and saved.

**Affected files:**

- Temporarily modify: `clip/clip/App.xaml.cs`
- Optionally inspect: `clip/clip/Core/Clipboard/ClipboardReader.cs`
- Optionally inspect: `clip/clip/Core/Storage/ClipboardItemRepository.cs`

**Instrumentation points:**

- After `ClipboardReader.ReadAsync()`: log whether draft is null, type, estimated bytes, and safe text length only.
- Before `SaveItemAsync()`: log draft type.
- After `SaveItemAsync()`: log returned id.
- Do not log clipboard plaintext.

**Expected evidence:**

- Marker clipboard event returns a non-null `ClipboardItemDraft` of type `Text`.
- `SaveItemAsync()` returns a positive id.
- `%LocalAppData%/WinClipboard/data.db` exists and contains the marker row.

**Safe DB check:**

```powershell
$db = Join-Path $env:LOCALAPPDATA "WinClipboard\data.db"
Test-Path $db
```

If a SQLite CLI is available:

```powershell
sqlite3 $db "select id, type, length(text_content), captured_at from items order by id desc limit 5;"
```

## Task D01.6: Root Cause Decision Table

Use the first true row as the root cause layer:

| Evidence | Root Cause Layer | Next Action |
|---|---|---|
| No intended `win-x64\clip.exe` process | Smoke harness/process isolation | Fix smoke launcher/process cleanup |
| `OnLaunched()` logs absent | App startup/WinAppSDK launch | Investigate executable mode and startup exception |
| `StorageService` constructor logs absent | Startup path before storage | Inspect `OnLaunched()` early exceptions |
| Constructor logs path but directory/DB absent | Storage path/permissions | Inspect `Directory.CreateDirectory` and SQLite open |
| `InitAsync()` starts but does not complete | Storage migration/open | Inspect `StorageConnection.OpenAsync()` and migrator exceptions |
| DB exists but no HWND/listener logs | Native host startup | Inspect `MessageOnlyWindowHost.Start()` and thread creation |
| Listener attaches but no `WM_CLIPBOARDUPDATE` | Win32 clipboard listener | Inspect HWND/message pump/AddClipboardFormatListener |
| `WM_CLIPBOARDUPDATE` fires but App callback absent | Event wiring/dispatcher | Inspect event subscription and `DispatcherQueue` |
| App callback runs but draft is null | Clipboard reader | Inspect `ClipboardReader.ReadAsync()` and clipboard formats |
| Draft is valid but no DB row | Repository/save path | Inspect `ClipboardItemRepository.SaveItemAsync()` |

## Completion Criteria

This debug card is complete only when:

- The failing layer is identified with logs or command output.
- The evidence is written to `docs/architecture/runtime-smoke-2026-05-23.md` or a follow-up debug note.
- A minimal fix task card is created for the identified layer.
- No speculative fix is applied before root cause is documented.

## Do Not Do Yet

- Do not rewrite storage paths without proving the app is actually initializing storage.
- Do not change clipboard listener logic before proving `WM_CLIPBOARDUPDATE` is missing.
- Do not add retries before identifying where the event/data flow stops.
- Do not treat Playwright static page success as proof that the WebView2 app runtime works.
