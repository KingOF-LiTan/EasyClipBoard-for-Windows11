# WinClipboard Boundary Decomposition Task Cards

> Role: Architect
>
> Goal: split the current oversized frontend and bridge boundaries into small, testable, behavior-preserving units. These cards are designed for incremental implementation. Do not rewrite behavior and structure in the same step unless the card explicitly says so.

## Current Boundary Problems

- `clip/clip/wwwroot/app.js` owns bridge calls, state, i18n, list rendering, drag behavior, card actions, preview, keyboard shortcuts, settings, vault, confirm modal, and window animation.
- `clip/clip/WebBridge.cs` owns action dispatch, business actions, settings persistence, native window operations, media loading, drag initiation, and frontend DTO mapping.
- `clip/clip/Core/Storage/StorageService.cs` owns schema migration, SQL execution, history queries, vault queries, encryption insert paths, blob paths, and cleanup.
- `clip/clip/Native/MessageOnlyWindowHost.cs` owns HWND lifecycle, tray, hotkeys, clipboard listener, hotkey rebinding, and plain-text paste.
- `clip/clip/wwwroot/index.html` still contains inline styles, inline event handlers, hardcoded Chinese, and mixed icon conventions.

## Shared Rules For All Cards

- Preserve WebView2 + Vanilla JS. Do not introduce Node, bundlers, TypeScript, or a frontend framework.
- Do not remove existing user-facing behavior while splitting files.
- Keep old global function names available until `index.html` inline handlers are removed.
- Any clipboard write path must keep suppress behavior via `_onBeforeClipboardWrite`, `SetSuppressFlag`, or `App.SuppressClipboardUpdate`.
- Any sensitive content path must keep masked list display and copy-time decryption only.
- After each card, update `system_map.md` if action flow, file ownership, or module boundaries changed.
- Validate dark theme, light theme, pure Mica/Acrylic mode, and custom background image mode for visible UI changes.

## Target Frontend File Map

- `clip/clip/wwwroot/app.js`: startup only.
- `clip/clip/wwwroot/js/bridge.js`: bridge request/response, timeout, JS error logging.
- `clip/clip/wwwroot/js/state.js`: central UI state and state helpers.
- `clip/clip/wwwroot/js/i18n.js`: `t()`, `applyTranslations()`, locale update.
- `clip/clip/wwwroot/js/history.js`: history/favorites loading, search parsing, tab switching.
- `clip/clip/wwwroot/js/listView.js`: card DOM rendering, lazy thumbnails, selected state rendering.
- `clip/clip/wwwroot/js/listActions.js`: select, paste, delete, favorite, tag, preview entry.
- `clip/clip/wwwroot/js/drag.js`: card drag-to-native behavior and anti-click shield.
- `clip/clip/wwwroot/js/keyboard.js`: global shortcuts and hotkey recorder.
- `clip/clip/wwwroot/js/settings.js`: theme, language, background, mask, autostart, shortcut settings.
- `clip/clip/wwwroot/js/vault.js`: sensitive vault list, add, copy username/password, delete.
- `clip/clip/wwwroot/js/overlays.js`: panel/modal open-close state, confirm, preview modal, window animation.

## Target Backend File Map

- `clip/clip/Bridge/WebBridge.cs`: JSON receive/send and dispatch only.
- `clip/clip/Bridge/BridgeRequest.cs`: parsed action request abstraction.
- `clip/clip/Bridge/BridgeResponse.cs`: consistent success/error response envelope.
- `clip/clip/Bridge/HistoryActions.cs`: history, favorites, delete, favorite, tag, clear history.
- `clip/clip/Bridge/VaultActions.cs`: sensitive item list, add, delete, decrypt, username, paste text.
- `clip/clip/Bridge/SettingsActions.cs`: settings, theme, language, background, mask, autostart, shortcut.
- `clip/clip/Bridge/MediaActions.cs`: thumbnail, full text, image preview.
- `clip/clip/Bridge/WindowActions.cs`: hide window, JS log, native drag.
- `clip/clip/Bridge/ClipboardItemDtoMapper.cs`: map storage entities to frontend DTOs.

---

## Task Card 01: Create Frontend Module Shell

**Goal:** Add the `wwwroot/js/` module files and load them from `index.html` without changing behavior.

**Affected files:**

- Modify: `clip/clip/wwwroot/index.html`
- Modify: `clip/clip/wwwroot/app.js`
- Create: `clip/clip/wwwroot/js/bridge.js`
- Create: `clip/clip/wwwroot/js/state.js`
- Create: `clip/clip/wwwroot/js/i18n.js`
- Create: `clip/clip/wwwroot/js/history.js`
- Create: `clip/clip/wwwroot/js/listView.js`
- Create: `clip/clip/wwwroot/js/listActions.js`
- Create: `clip/clip/wwwroot/js/drag.js`
- Create: `clip/clip/wwwroot/js/keyboard.js`
- Create: `clip/clip/wwwroot/js/settings.js`
- Create: `clip/clip/wwwroot/js/vault.js`
- Create: `clip/clip/wwwroot/js/overlays.js`

**Steps:**

1. Create `window.WinClipboard = window.WinClipboard || {}` in `state.js`.
2. Add empty namespaces: `bridge`, `state`, `i18n`, `history`, `listView`, `listActions`, `drag`, `keyboard`, `settings`, `vault`, `overlays`.
3. Load scripts before `app.js` in this order: state, bridge, i18n, overlays, history, listView, listActions, drag, keyboard, settings, vault, app.
4. Keep all existing functions in `app.js` for this card.

**Risk:** Script load order can break globals if `app.js` runs before namespace files.

**Verification:**

- `dotnet build clip/clip.slnx`
- Launch app and verify history list loads.
- Verify existing buttons still call their inline `onclick` handlers.

---

## Task Card 02: Extract Bridge Communication

**Goal:** Move bridge request/response handling out of `app.js`.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/bridge.js`

**Move from `app.js`:**

- `requestCounter`
- `pendingRequests`
- `sendMessage(action, params)`
- `window.__bridge_response`
- `window.__on_clipboard_updated`
- `window.onerror`
- `console.error` proxy
- `window.addEventListener('error', ...)`
- `window.addEventListener('unhandledrejection', ...)`

**Required public API:**

- `WinClipboard.bridge.send(action, params)`
- `window.sendMessage = WinClipboard.bridge.send` as compatibility shim.

**Behavior rule:** Existing callers should continue using `sendMessage` until their owning modules are extracted.

**Risk:** Timeout behavior for `selectBackgroundImage` must remain special because the file picker can take longer than five seconds.

**Verification:**

- Trigger `getSettings`, `getHistory`, `paste`, and `log`.
- Simulate a bad JS error and confirm it still reaches C# debug logging.

---

## Task Card 03: Extract State Model

**Goal:** Replace scattered top-level mutable variables with explicit state helpers.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/state.js`

**Move from `app.js`:**

- `currentTab`
- `items`
- `selectedIndex`
- `settingsOpen`
- `vaultOpen`
- `currentTheme`
- `thumbnailCache`
- `maskSaveTimeout` may stay in settings until Task 09.

**Required public API:**

- `WinClipboard.state.get()`
- `WinClipboard.state.set(patch)`
- `WinClipboard.state.setItems(items)`
- `WinClipboard.state.getItems()`
- `WinClipboard.state.setSelectedIndex(index)`
- `WinClipboard.state.getSelectedItem()`
- `WinClipboard.state.setActivePanel(panelNameOrNull)`
- `WinClipboard.state.setActiveModal(modalNameOrNull)`

**Behavior rule:** Keep `window.currentTab`, `window.items`, and `window.selectedIndex` compatibility only if existing inline/debug code still depends on them. Prefer removing them after all modules are extracted.

**Risk:** `renderList()` and keyboard navigation both mutate selection; missing a state update will make UI selection drift.

**Verification:**

- Switch history/favorites.
- Search with non-empty and empty result.
- ArrowUp/ArrowDown updates the selected card.
- `window.__on_window_shown()` refreshes the list.

---

## Task Card 04: Extract i18n

**Goal:** Move translation helpers and locale re-application out of `app.js`.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/i18n.js`
- Later modify: `clip/clip/WebBridge.cs` or `clip/clip/Bridge/SettingsActions.cs` if script callback name changes.

**Move from `app.js`:**

- `window.t`
- `applyTranslations()`

**Required public API:**

- `WinClipboard.i18n.t(key)`
- `WinClipboard.i18n.applyTranslations()`
- `window.t = WinClipboard.i18n.t`
- `window.applyTranslations = WinClipboard.i18n.applyTranslations`

**Risk:** `setLanguage` currently executes JS that calls `applyTranslations()`. Keep the global function until the backend script is updated.

**Verification:**

- Toggle language in settings.
- Confirm navbar, search placeholder, settings labels update without reload.

---

## Task Card 05: Extract History Loading And Tab Switching

**Goal:** Separate data loading from DOM rendering and action handling.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/history.js`
- Modify: `clip/clip/wwwroot/js/state.js`
- Modify: `clip/clip/wwwroot/js/listView.js`

**Move from `app.js`:**

- `switchTab(tab)`
- `refreshList()`
- smart search parsing for `/img` and `/file`

**Required public API:**

- `WinClipboard.history.switchTab(tab)`
- `WinClipboard.history.refreshList()`
- `WinClipboard.history.parseSearch(rawSearch)`
- `window.switchTab = WinClipboard.history.switchTab`
- `window.refreshList = WinClipboard.history.refreshList`

**Boundary:** This module fetches data and updates state. It must call `WinClipboard.listView.render()` but must not build card HTML itself.

**Risk:** Favorites currently accepts a `category` parameter in bridge but UI only sends search. Do not add category UI during extraction.

**Verification:**

- History tab loads non-favorite, non-sensitive items.
- Favorites tab loads favorites.
- `/img` filters image cards.
- `/file` filters file cards.
- Search debounce still refreshes list.

---

## Task Card 06: Extract List View Rendering

**Goal:** Make list rendering a pure-ish view module that reads state and emits DOM.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/listView.js`
- Modify: `clip/clip/wwwroot/js/listActions.js`

**Move from `app.js`:**

- `renderList()`
- `replacePlaceholder(placeholder, base64, idx)`
- `getTagIcon(tag)`
- `getTypeIcon(type)`
- `escapeHtml(str)`
- `scrollToSelected()`

**Required public API:**

- `WinClipboard.listView.render()`
- `WinClipboard.listView.scrollToSelected()`
- `WinClipboard.listView.escapeHtml(str)`
- `window.renderList = WinClipboard.listView.render`

**Boundary:** `listView` may attach DOM event hooks or compatibility `onclick` strings, but actual actions must delegate to `listActions`.

**Risk:** The current renderer uses full `innerHTML` replacement and an `IntersectionObserver`. Preserve observer disconnect behavior to avoid duplicate thumbnail loads.

**Verification:**

- Text, image, and file cards render.
- Lazy thumbnails load only when visible.
- Selected ring follows keyboard selection.
- Empty state appears for empty result.

---

## Task Card 07: Extract List Actions And Define Interaction Semantics

**Goal:** Centralize card actions and make click/paste/preview/delete/favorite/tag responsibilities explicit.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/listActions.js`
- Modify: `clip/clip/wwwroot/js/listView.js`

**Move from `app.js`:**

- `onCardClick(idx)`
- `pasteItem(idx)`
- `deleteItem(idx)`
- `toggleFavorite(idx)`
- `setTag(idx, tag)`
- `showPreview(idx)` only if preview is not yet moved to overlays in Task 08.

**Phase A behavior:** Preserve current behavior: single click selects and pastes.

**Phase B behavior proposal:** Change to stable semantics after Phase A passes:

- Single click: select only.
- Enter: paste selected item.
- Number 1-9: quick paste.
- Space: preview selected item.
- Explicit paste icon/button: paste card.
- Drag gesture: drag only, no paste.

**Required public API:**

- `WinClipboard.listActions.select(idx)`
- `WinClipboard.listActions.onCardClick(idx)`
- `WinClipboard.listActions.paste(idx)`
- `WinClipboard.listActions.delete(idx)`
- `WinClipboard.listActions.toggleFavorite(idx)`
- `WinClipboard.listActions.setTag(idx, tag)`
- Compatibility globals for existing inline handlers.

**Risk:** Changing click semantics is user-visible. Do Phase A first, then decide whether to enable Phase B.

**Verification:**

- Click behavior matches the chosen phase.
- Delete refreshes list.
- Favorite moves item between history/favorites correctly.
- Tag update persists and re-renders.

---

## Task Card 08: Extract Overlays And Modal State

**Goal:** Put preview, confirm, panel open/close, and window animation under one overlay controller.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/overlays.js`
- Modify: `clip/clip/wwwroot/js/settings.js`
- Modify: `clip/clip/wwwroot/js/vault.js`
- Modify: `clip/clip/wwwroot/js/listActions.js`

**Move from `app.js`:**

- `showPreview(idx)`
- `closePreview()`
- `showConfirm(message, onConfirm)`
- `playShowAnimation()`
- `playHideAnimation(callback)`
- `hideWindowAnimated()`
- `window.__on_window_shown`
- panel visibility logic from `toggleSettings()` and `toggleVault()`

**Required public API:**

- `WinClipboard.overlays.openPanel(panelName)`
- `WinClipboard.overlays.closePanel(panelName)`
- `WinClipboard.overlays.togglePanel(panelName)`
- `WinClipboard.overlays.openModal(modalName)`
- `WinClipboard.overlays.closeModal(modalName)`
- `WinClipboard.overlays.showPreview(idx)`
- `WinClipboard.overlays.showConfirm(message, onConfirm)`
- `WinClipboard.overlays.hideWindowAnimated()`
- Compatibility globals: `showPreview`, `closePreview`, `hideWindowAnimated`, `__on_window_shown`.

**Risk:** Escape handling depends on modal/panel priority. Define priority as: preview modal, vault add modal, confirm modal, settings panel, vault panel, then hide window.

**Verification:**

- Escape closes the top-most visible overlay first.
- Settings and vault panels remain mutually exclusive.
- Preview for text and image still works.
- Hide animation still calls backend `hideWindow`.

---

## Task Card 09: Extract Settings

**Goal:** Move settings UI and persistence calls out of `app.js`.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/settings.js`
- Modify: `clip/clip/wwwroot/js/keyboard.js` for hotkey recorder boundary.

**Move from `app.js`:**

- `loadSettings()`
- `toggleSettings()` after panel mechanics are delegated to overlays
- `changeTheme(value)`
- `changeLanguage(value)`
- `pickBackground()`
- `clearBackground()`
- `setBgImage(base64Data)`
- `changeMaskOpacity(value)`
- `updateMaskVisual(opacity)`
- autostart toggle wiring

**Required public API:**

- `WinClipboard.settings.load()`
- `WinClipboard.settings.changeTheme(value)`
- `WinClipboard.settings.changeLanguage(value)`
- `WinClipboard.settings.pickBackground()`
- `WinClipboard.settings.clearBackground()`
- `WinClipboard.settings.setBgImage(base64Data)`
- `WinClipboard.settings.changeMaskOpacity(value)`
- Compatibility globals for inline handlers.

**Risk:** `getSettings()` currently returns hardcoded `maskOpacity = 0.3` while `setMaskOpacity` persists `ui_background_mask_opacity`. Log this as a backend follow-up, do not silently change behavior in the extraction card.

**Verification:**

- Theme changes JS theme and native DWM theme.
- Language updates without reload.
- Background image picker still works.
- Clear background returns to pure backdrop mode.
- Mask slider changes visible overlay and persists through bridge call.
- Autostart toggle still calls registry action.

---

## Task Card 10: Extract Keyboard And Hotkey Recorder

**Goal:** Separate list keyboard navigation from settings hotkey recording.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/keyboard.js`
- Modify: `clip/clip/wwwroot/js/settings.js`
- Modify: `clip/clip/wwwroot/js/overlays.js`

**Move from `app.js`:**

- `setupKeyboardShortcuts()`
- global `window._mods` tracking
- shortcut input focus/blur/keydown logic
- search input debounce may move here or stay in history; prefer `history.bindSearchInput()`.

**Required public API:**

- `WinClipboard.keyboard.init()`
- `WinClipboard.keyboard.bindGlobalShortcuts()`
- `WinClipboard.keyboard.bindShortcutRecorder(inputElement)`
- `WinClipboard.keyboard.getModifierMask(event)`

**Boundary:** `keyboard.js` decides intent and delegates to `history`, `listActions`, `overlays`, or `settings`. It must not directly rebuild card HTML except through those modules.

**Risk:** WebView2 accelerator suppression makes modifier tracking fragile. Keep capture-phase modifier tracking exactly as current behavior until tests pass.

**Verification:**

- Escape closes overlays in priority order.
- Ctrl+F focuses search.
- ArrowUp/ArrowDown navigates while search is focused.
- Enter pastes selected.
- Space previews selected.
- 1-9 quick-pastes.
- Shortcut recorder captures Ctrl/Alt/Shift/Win combinations.

---

## Task Card 11: Extract Vault

**Goal:** Move sensitive vault behavior and rendering out of `app.js`.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/js/vault.js`
- Modify: `clip/clip/wwwroot/js/overlays.js`

**Move from `app.js`:**

- `toggleVault()`
- `refreshVault(search)`
- `renderVault(vaultItems)`
- `getSensitiveIcon(type)`
- `copySecret(id)`
- `copyUsername(id)`
- `deleteSecret(id)`
- `searchVault(query)`
- `addSecret()`
- `closeVaultAddModal()`
- `submitVaultAdd()`

**Required public API:**

- `WinClipboard.vault.toggle()`
- `WinClipboard.vault.refresh(search)`
- `WinClipboard.vault.render(items)`
- `WinClipboard.vault.copySecret(id)`
- `WinClipboard.vault.copyUsername(id)`
- `WinClipboard.vault.deleteSecret(id)`
- Compatibility globals for existing inline handlers.

**Risk:** Vault copy decrypts then writes to clipboard through `pasteText`; suppress behavior must remain on the C# bridge path.

**Verification:**

- Open vault panel.
- Search vault items.
- Add item with alias, username, password, and remark.
- Copy username.
- Copy password.
- Delete item.
- Confirm sensitive text is not visible in normal history list.

---

## Task Card 12: Remove Frontend Inline Styles And Hardcoded Text

**Goal:** Move inline visual styling and hardcoded user-facing strings from `index.html` and generated HTML into CSS and locale files.

**Affected files:**

- Modify: `clip/clip/wwwroot/index.html`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `clip/clip/wwwroot/locales/zh.json`
- Modify: `clip/clip/wwwroot/locales/en.json`
- Modify: generated HTML in `listView.js` and `vault.js`

**Scope:**

- Move inline `style=` from settings shortcut input, vault add modal fields, confirm modal, and generated vault remark.
- Replace hardcoded Chinese in vault panel, add modal, confirm modal, alerts, and button titles with i18n keys.
- Normalize icon source later; this card only removes inline styling and strings.

**Risk:** Generated HTML strings can regress spacing or contrast in transparent background modes.

**Verification:**

- Check dark/light pure backdrop.
- Check dark/light custom background image.
- Verify all translated labels still appear after language toggle.
- Verify no essential control loses focus/hover/disabled styling.

---

## Task Card 13: Split WebBridge Dispatch From Actions

**Goal:** Make `WebBridge.cs` a thin router instead of a business logic container.

**Affected files:**

- Modify or move: `clip/clip/WebBridge.cs`
- Create: `clip/clip/Bridge/WebBridge.cs`
- Create: `clip/clip/Bridge/BridgeRequest.cs`
- Create: `clip/clip/Bridge/BridgeResponse.cs`
- Create: `clip/clip/Bridge/HistoryActions.cs`
- Create: `clip/clip/Bridge/VaultActions.cs`
- Create: `clip/clip/Bridge/SettingsActions.cs`
- Create: `clip/clip/Bridge/MediaActions.cs`
- Create: `clip/clip/Bridge/WindowActions.cs`
- Create: `clip/clip/Bridge/ClipboardItemDtoMapper.cs`
- Modify: `clip/clip/MainWindow.xaml.cs`

**Steps:**

1. Introduce `BridgeResponse.Ok(data)` and `BridgeResponse.Fail(error)`.
2. Keep frontend payload shape compatible for existing actions during the first backend split.
3. Move `MapEntity` into `ClipboardItemDtoMapper`.
4. Move action methods into action classes without changing action names.
5. Make `WebBridge.HandleMessageAsync` parse action and route to handlers.
6. On exception, always send a response when `requestId` exists.

**Risk:** The current frontend expects some actions to return raw lists and some to return `{ success }`. Do not normalize all frontend consumers in the same card unless every call site is updated together.

**Verification:**

- Exercise every action listed in `system_map.md`.
- Force an unknown action and confirm frontend receives a structured error.
- Confirm `selectBackgroundImage` still waits for picker result.

---

## Task Card 14: Create Bridge Action Protocol Table

**Goal:** Document request and response contracts so frontend and backend stop drifting.

**Affected files:**

- Modify: `system_map.md`
- Create: `docs/architecture/bridge-action-protocol.md`

**Required columns:**

- Action name
- Owner class/module
- Request fields
- Success response shape
- Failure response shape
- Clipboard write suppress needed
- Sensitive content exposure rule
- Frontend caller module

**Risk:** Protocol docs become stale unless updated when actions change. Add "update protocol" to future bridge cards.

**Verification:**

- Every current action in `WebBridge.cs` appears in the table.
- Every frontend `sendMessage(...)` call maps to a protocol row.

---

## Task Card 15: Split Storage Service

**Goal:** Separate persistence concerns after bridge callers are stable.

**Affected files:**

- Modify: `clip/clip/Core/Storage/StorageService.cs`
- Create: `clip/clip/Core/Storage/StorageConnection.cs`
- Create: `clip/clip/Core/Storage/StorageMigrator.cs`
- Create: `clip/clip/Core/Storage/ClipboardItemRepository.cs`
- Create: `clip/clip/Core/Storage/VaultRepository.cs`
- Create: `clip/clip/Core/Storage/BlobStore.cs`
- Modify: bridge action classes that call storage.
- Modify: `clip/clip/App.xaml.cs`

**Boundary target:**

- `StorageConnection`: SQLite connection, lock, exec/query helpers.
- `StorageMigrator`: table creation, ALTER TABLE, indexes.
- `BlobStore`: blob directory, SHA256, image write/delete/full path.
- `ClipboardItemRepository`: save captured item, history/favorites/search/tag/delete/clear/purge.
- `VaultRepository`: add manual secret, list sensitive, decrypt sensitive, update alias.
- `StorageService`: facade during migration only; remove once all callers use repositories.

**Risk:** This has higher blast radius than frontend split because clipboard capture, bridge actions, purge, OCR update, and image preview all call storage.

**Verification:**

- Build.
- Copy text, image, and files.
- Search history and favorites.
- Delete image and confirm blob removal.
- Purge old non-favorite, non-sensitive items.
- Add/copy/delete vault item.

---

## Task Card 16: Split Native Message Host Responsibilities

**Goal:** Reduce `MessageOnlyWindowHost.cs` to HWND lifecycle and message dispatch.

**Affected files:**

- Modify: `clip/clip/Native/MessageOnlyWindowHost.cs`
- Create: `clip/clip/Native/GlobalHotkeyService.cs`
- Create: `clip/clip/Native/PlainTextPasteService.cs`
- Create: `clip/clip/Native/ClipboardListenerService.cs`
- Existing: `clip/clip/Native/TrayIconService.cs`
- Existing: `clip/clip/Native/Win32Helper.cs`

**Boundary target:**

- `MessageOnlyWindowHost`: starts message thread, owns HWND, dispatches WndProc messages.
- `GlobalHotkeyService`: register initial hotkeys, rebind main hotkey, handle WM_HOTKEY intent.
- `PlainTextPasteService`: Ctrl+Shift+V read/write Unicode text and suppress history.
- `ClipboardListenerService`: add/remove clipboard listener and raise update event.
- `TrayIconService`: remains tray lifecycle and tray message handling.

**Risk:** Hotkey registration is thread-affine. Rebind must still marshal to the HWND thread.

**Verification:**

- Ctrl+Tab toggles main UI.
- Rebinding shortcut works and persists.
- Ctrl+Shift+V plain-text paste works.
- Clipboard updates still fire.
- Tray left click toggles UI.
- Tray right menu show/settings/exit works.

---

## Task Card 17: Decide And Implement Final Card Click Semantics

**Goal:** Resolve the current conflict between click-to-paste, selection, preview, and drag.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/listActions.js`
- Modify: `clip/clip/wwwroot/js/listView.js`
- Modify: `clip/clip/wwwroot/js/keyboard.js`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `system_map.md`

**Recommended final semantics:**

- Single click selects.
- Double click or explicit paste button pastes.
- Enter pastes selected.
- 1-9 quick-pastes.
- Space previews selected.
- Drag starts native drag only after threshold and never triggers paste.

**Alternative if speed is more important than discoverability:**

- Single click keeps paste.
- Hover action provides preview.
- Drag threshold remains high.
- Selected state is mostly keyboard-only.

**Architect recommendation:** Use single click select. It makes preview, drag, and future multi-action UI more predictable.

**Risk:** Existing users may rely on click-to-paste speed. Consider adding a setting later, but do not add that setting in this card.

**Verification:**

- Mouse click selects only.
- Enter and 1-9 paste.
- Drag does not paste.
- Image click previews or uses explicit preview depending on UI decision.

---

## Task Card 18: Final Cleanup And Dead Code Removal

**Goal:** Remove temporary compatibility shims after modules and HTML handlers are migrated.

**Affected files:**

- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/index.html`
- Modify: all `clip/clip/wwwroot/js/*.js`
- Modify: `system_map.md`

**Scope:**

- Replace inline `onclick` handlers with `addEventListener` bindings.
- Remove `window.*` compatibility globals that are no longer needed by C# callbacks.
- Keep only required C# callback globals:
  - `window.__bridge_response`
  - `window.__on_clipboard_updated`
  - `window.__on_window_shown`
  - `window.hideWindowAnimated` if C# still invokes it by name.
- Shrink `app.js` to startup initialization.

**Risk:** Removing globals too early breaks C# `ExecuteScriptAsync` callbacks or inline handlers.

**Verification:**

- Search frontend for `onclick=`.
- Search frontend for `window.` exports and confirm each is intentional.
- Run full verification matrix from `system_map.md`.

## Suggested Execution Order

1. Task 01: Create Frontend Module Shell
2. Task 02: Extract Bridge Communication
3. Task 03: Extract State Model
4. Task 04: Extract i18n
5. Task 05: Extract History Loading And Tab Switching
6. Task 06: Extract List View Rendering
7. Task 07 Phase A: Extract List Actions without behavior change
8. Task 08: Extract Overlays And Modal State
9. Task 09: Extract Settings
10. Task 10: Extract Keyboard And Hotkey Recorder
11. Task 11: Extract Vault
12. Task 12: Remove Frontend Inline Styles And Hardcoded Text
13. Task 13: Split WebBridge Dispatch From Actions
14. Task 14: Create Bridge Action Protocol Table
15. Task 17: Decide And Implement Final Card Click Semantics
16. Task 15: Split Storage Service
17. Task 16: Split Native Message Host Responsibilities
18. Task 18: Final Cleanup And Dead Code Removal

## Full Regression Matrix

- Build: `dotnet build clip/clip.slnx`
- Hotkey: main hotkey toggles UI at cursor.
- Hotkey: rebound shortcut persists and works.
- Hotkey: Ctrl+Shift+V plain-text paste works.
- Window: Escape hides UI when no overlay is open.
- Clipboard capture: text, image, and file copies appear in history.
- Search: normal text, `/img`, `/file`, OCR text if present.
- List keyboard: ArrowUp, ArrowDown, Enter, Space, 1-9, Ctrl+F.
- List mouse: click, explicit paste, favorite, delete, tag, drag.
- Vault: add, search, copy username, copy password, delete.
- Security: sensitive items remain masked in normal list and logs do not print plaintext.
- Settings: language, theme, background image, clear background, mask opacity, autostart, shortcut recorder.
- Visual: dark pure backdrop, light pure backdrop, dark image background, light image background.
