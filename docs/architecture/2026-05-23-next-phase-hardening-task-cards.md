# WinClipboard Next Phase Hardening Task Cards

> Role: Architect
>
> Goal: after boundary decomposition, stabilize runtime behavior, remove temporary compatibility seams, and make verification repeatable before larger feature work resumes.

## Review Baseline

- Frontend is now split under `clip/clip/wwwroot/js/`.
- `clip/clip/wwwroot/app.js` is now startup-only.
- Bridge actions are split under `clip/clip/Bridge/`.
- Storage is split behind `StorageService` facade.
- Native message host delegates hotkey, clipboard listener, and plain-text paste work.
- C# compile verification passes with `powershell -ExecutionPolicy Bypass -File scripts/verify-build.ps1`.
- Default `.slnx` verification currently fails in the installed SDK, and default project build fails in MSIX packaging.

## Stage Gate Before Feature Work

Do not start new product features until these are true:

- The app builds with a documented local command.
- Manual smoke checks cover clipboard capture, paste, drag, vault, settings, and hotkeys.
- `system_map.md` matches the implemented boundaries.
- Temporary compatibility globals and inline handlers are either documented as intentional or removed.
- Bridge action response shapes are documented and consistent enough for frontend error handling.

---

## Task Card H01: Fix And Document Build Verification

**Goal:** Make build verification reliable for local agents.

**Affected files:**

- Modify: `system_map.md`
- Modify: `docs/architecture/bridge-action-protocol.md` if build notes affect action validation workflow.
- Optional modify: `clip/clip/clip.csproj` only if the project should support non-MSIX local builds by default.

**Steps:**

1. Keep `powershell -ExecutionPolicy Bypass -File scripts/verify-build.ps1` as the current compile verification command.
2. Investigate why `dotnet build clip/clip.slnx` fails with `MSB4068`.
3. Investigate why default project build fails in `WinAppSdkGenerateAppxPackageRecipe`.
4. Decide whether local development should use a documented property or a checked-in build script.

**Verification:**

- Compile command exits 0.
- Default build failure is either fixed or documented as packaging-only.

---

## Task Card H02: Runtime Smoke Pass

**Goal:** Validate the refactor in the real WebView2 app.

**Affected files:**

- No source edits unless bugs are found.
- Update: `system_map.md` with any confirmed behavior changes.

**Manual checks:**

- Launch app.
- Main hotkey toggles UI at cursor.
- Text copy appears in history.
- Image copy appears in history and thumbnail lazy-loads.
- File copy appears in history.
- Single click selects.
- Double-click or paste button pastes.
- Enter pastes selected.
- Space previews selected.
- 1-9 quick-pastes.
- Drag starts native drag and does not paste.
- Favorite, delete, tag, and clear history work.
- Vault add/search/copy username/copy password/delete work.
- Language, theme, background image, clear background, mask opacity, autostart, shortcut recorder work.
- Ctrl+Shift+V plain-text paste works in an external target app.

**Verification output:**

- Record pass/fail notes in `docs/architecture/runtime-smoke-2026-05-23.md`.

---

## Task Card H03: Remove Accidental Workspace Artifact

**Goal:** Decide what to do with the untracked `.vscode/nul` artifact.

**Affected files:**

- Inspect: `.vscode/nul`
- Optional modify: `.gitignore`

**Steps:**

1. Confirm whether `.vscode/nul` is a real file, Windows device alias, or tool artifact.
2. If accidental, remove it using a Windows-safe literal path command.
3. If it cannot be removed normally, document the cleanup command.
4. Add a `.gitignore` rule only if this artifact can recur.

**Verification:**

- `git status --short` no longer shows `.vscode/nul`, or the artifact is explicitly documented.

---

## Task Card H04: Normalize Bridge Success And Error Handling

**Goal:** Make frontend callers handle bridge failure predictably.

**Affected files:**

- Modify: `clip/clip/Bridge/BridgeResponse.cs`
- Modify: `clip/clip/Bridge/*Actions.cs`
- Modify: `clip/clip/wwwroot/js/bridge.js`
- Modify: frontend caller modules as needed.
- Update: `docs/architecture/bridge-action-protocol.md`

**Steps:**

1. Add `BridgeResponse.Ok(object? data = null)` or intentionally document why raw list actions remain raw.
2. Decide whether list actions should return raw arrays or `{ success: true, items }`.
3. Add frontend helper checks for `{ success: false, error }`.
4. Keep compatibility with current callers during transition.

**Risk:**

- Changing `getHistory`, `getFavorites`, or `getSensitiveItems` response shape requires updating all frontend list callers together.

**Verification:**

- Unknown action returns a visible or logged structured error.
- Timeout still resolves safely.
- History/favorites/vault still render.

---

## Task Card H05: Retire Inline Handlers Incrementally

**Goal:** Move event binding out of `index.html` and generated HTML once module boundaries are stable.

**Affected files:**

- Modify: `clip/clip/wwwroot/index.html`
- Modify: `clip/clip/wwwroot/js/*.js`

**Steps:**

1. Replace static `onclick`, `onchange`, and `oninput` in `index.html` with event binding in module init functions.
2. Replace generated card action inline handlers with `addEventListener` after `listView.render()`.
3. Replace generated vault action inline handlers with event delegation.
4. Remove globals that only existed for inline handlers.
5. Keep C# callback globals: `__bridge_response`, `__on_clipboard_updated`, `__on_window_shown`, `hideWindowAnimated` unless backend invocation changes.

**Verification:**

- `Select-String -Path clip/clip/wwwroot/index.html -Pattern "onclick|onchange|oninput"` returns no event handlers.
- Static controls and generated cards still work.

---

## Task Card H06: Add Frontend Smoke Syntax Script

**Goal:** Make JS syntax verification repeatable without adding a build system.

**Affected files:**

- Create: `scripts/check-frontend-js.ps1`
- Update: `system_map.md`

**Script behavior:**

- Run `node --check` on `clip/clip/wwwroot/app.js`.
- Run `node --check` on every `clip/clip/wwwroot/js/*.js`.
- Exit nonzero on the first syntax error.

**Verification:**

- `powershell -ExecutionPolicy Bypass -File scripts/check-frontend-js.ps1` exits 0.

---

## Task Card H07: Native Hotkey Regression Check

**Goal:** Confirm the native service split preserved thread-affine behavior.

**Affected files:**

- Inspect: `clip/clip/Native/MessageOnlyWindowHost.cs`
- Inspect: `clip/clip/Native/GlobalHotkeyService.cs`
- Inspect: `clip/clip/Native/PlainTextPasteService.cs`

**Focus areas:**

- `Rebind()` posts `WM_APP_REBIND` to the HWND thread.
- Plain-text paste captures and restores the correct target window.
- Cleanup unregisters both main hotkey and Ctrl+Shift+V.
- Clipboard listener attach/detach happens on the message HWND.

**Verification:**

- Main hotkey works after startup.
- Rebound hotkey works without restarting.
- Ctrl+Shift+V pastes into the original target app.
- Exit removes tray icon and hotkeys.

---

## Suggested Execution Order

1. H01: Fix And Document Build Verification
2. H02: Runtime Smoke Pass
3. H03: Remove Accidental Workspace Artifact
4. H04: Normalize Bridge Success And Error Handling
5. H05: Retire Inline Handlers Incrementally
6. H06: Add Frontend Smoke Syntax Script
7. H07: Native Hotkey Regression Check
