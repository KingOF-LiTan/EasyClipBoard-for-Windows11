# WinClipboard Runtime UX Feedback Task Cards

> Role: Architect
>
> Source: real user smoke on 2026-05-24.
>
> Goal: fix the interaction points that still feel non-responsive or blocked during actual use.
>
> Execution owner: Implementer. Architect only defines tasks, boundaries, and acceptance criteria.

## Feedback Summary

User-reported issues:

1. Double-clicking a card has no visible effect.
2. Enter paste works, but the window waits for the success toast before closing; it feels slow.
3. Text preview is hard to exit, text cannot be selected, and preview needs richer interaction.
4. Dragging can get stuck; image history should drag into QQ, Codex, and other editable targets like a normal image/file.
5. The summoned window cannot be moved.
6. Escape close works, toast works, but `?` help does not open.

## Boundary Rules

- Keep single-click semantics: single click selects only.
- Keep Enter and 1-9 paste semantics.
- Do not reintroduce inline event handlers.
- Do not solve drag by pasting through clipboard; drag must stay a native drag/drop operation.
- Do not make the whole WebView draggable, because that would break card selection, text selection, search, and buttons.
- Any runtime or native change must still pass:
  - `scripts/verify-build.ps1`
  - `scripts/check-frontend-js.ps1`
  - `scripts/check-interaction-affordance.ps1`
  - `scripts/check-runtime-smoke.ps1`

---

## Task FB01: Restore Double-Click Paste

**Goal:** Double-clicking a card reliably pastes the item and hides the window, without changing single-click selection.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/listView.js`
- Modify: `clip/clip/wwwroot/js/listActions.js`
- Modify: `clip/clip/wwwroot/js/drag.js`
- Modify: `scripts/check-interaction-affordance.ps1`

**Current suspicion:**

- `drag.js` installs document-level pointer handling and an anti-click shield.
- Card click/double-click and drag detection now overlap.
- Image card click also opens preview, which may make image-card double-click feel inert or inconsistent.
- Stronger root-cause hypothesis after runtime feedback: `onCardClick()` calls `select()`, and `select()` calls `render()`, which rebuilds the card DOM after the first click. That can prevent the browser from dispatching a native `dblclick` event against the same element.

**Interaction contract:**

- Single click on any non-action card area selects.
- Double-click on any non-action card area pastes even if the first click caused list re-render.
- Double-click on image card pastes unless the explicit preview button is clicked.
- Double-click must not be blocked by the drag anti-click shield unless a drag threshold was actually crossed.
- Double-click on action buttons must keep the action button behavior, not paste the card.
- Do not rely solely on the browser `dblclick` event for card paste. Implement a small click-timing detector keyed by item id or idx in `listActions.js`, or otherwise avoid DOM replacement between the two clicks.

**Acceptance criteria:**

- Double-click a text card: item is pasted and window hides.
- Double-click an image card body: image item is pasted and window hides.
- Double-click a file card: file item is pasted and window hides.
- Double-click an unselected card works.
- Double-click a selected card works.
- Single click still only selects.
- Explicit preview button still previews.
- `check-interaction-affordance.ps1` verifies that:
  - card double-click paste does not depend only on `addEventListener('dblclick', ...)`.
  - `onCardClick` or the click path contains a guarded second-click detector.
  - drag shield is only enabled after threshold crossing.
  - image click no longer steals the double-click paste path.

**Manual verification:**

- Select with one click, then double-click the same card.
- Double-click an unselected card.
- Try double-clicking near the image thumbnail and near card text.
- Verify no accidental drag state remains after double-click.

---

## Task FB02: Make Paste Close Immediate

**Goal:** Pasting should feel instant; the window must not wait for toast display before hiding.

**Status:** Reported completed by implementer. Keep this task as a regression guard.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/listActions.js`
- Modify: `clip/clip/wwwroot/js/vault.js`
- Modify: `clip/clip/wwwroot/js/toast.js`
- Modify: `scripts/check-interaction-affordance.ps1`

**Interaction contract:**

- After successful normal paste, hide the window immediately.
- Do not use an 800ms delay on normal paste.
- Paste toast must not block close. It may be skipped for paste, shown only briefly before close, or replaced by a non-blocking next-open status, but it cannot delay hide.
- Vault copy should also not feel delayed; if sensitive copy hides the window, hide immediately after the copy succeeds.
- Failure cases must not hide the window or show success toast.

**Acceptance criteria:**

- Enter paste hides the window within 120ms after bridge `paste` success.
- Paste button hides the window within 120ms after bridge `paste` success.
- 1-9 quick paste hides the window within 120ms after bridge `paste` success.
- No `setTimeout(..., 800)` or equivalent paste-close delay remains in paste paths.
- Success toast never gates `hideWindow`.

**Manual verification:**

- Press Enter on selected card; window disappears immediately.
- Press 1 on first card; window disappears immediately.
- Click paste arrow; window disappears immediately.
- Temporarily simulate bridge paste failure if possible; window remains open and no success toast appears.

---

## Task FB03: Improve Text Preview Exit And Selection

**Goal:** Text preview should be easy to inspect, select, copy, and close.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/overlays.js`
- Modify: `clip/clip/wwwroot/js/keyboard.js`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `clip/clip/wwwroot/index.html`
- Modify: `clip/clip/wwwroot/locales/zh.json`
- Modify: `clip/clip/wwwroot/locales/en.json`
- Modify: `scripts/check-interaction-affordance.ps1`

**Interaction contract:**

- Preview text must be selectable.
- The preview content area must allow normal text selection gestures.
- Right-click closes preview when no text selection is active.
- Right-click must not destroy an active selection before the user can copy through keyboard.
- Escape still closes preview.
- Clicking the backdrop can continue to close preview, but clicking/dragging inside text must not close it.
- Add a compact close affordance in preview for discoverability if it does not crowd the view.

**Acceptance criteria:**

- Open text preview, drag across text: selection appears.
- Press Ctrl+C after selecting preview text: selected text is copied by the WebView/browser selection path.
- Right-click preview with no active selection: preview closes.
- Right-click after selecting text: selection remains long enough for Ctrl+C; preview does not immediately destroy it.
- Escape closes preview.
- Preview modal still supports file full-path text.

**Manual verification:**

- Preview long text, select a paragraph, Ctrl+C into Notepad.
- Preview file item, select part of a path, Ctrl+C into Notepad.
- Right-click empty/backdrop area closes preview.
- Right-click selected text does not clear selection unexpectedly.

---

## Task FB04: Fix Native Drag Reliability And Image Drag

**Goal:** Dragging cards into QQ, Codex, and editable targets should not freeze, and image history should drag as usable image/file data.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/drag.js`
- Modify: `clip/clip/Bridge/WindowActions.cs`
- Modify: `clip/clip/Native/DragDropService.cs`
- Modify: `clip/clip/Native/Win32Helper.cs` if additional constants/helpers are required.
- Modify: `scripts/check-interaction-affordance.ps1`

**Current risk points:**

- `DragDropService.StartDrag()` runs a blocking native OLE drag loop.
- `WindowActions.StartDragAsync()` synthesizes mouse up/down before `DoDragDrop`, which can leave pointer state feeling stuck.
- Image drag currently relies on saved blob path as `CF_HDROP`; some editors accept pasted image data but not a file drop, or require the file extension/MIME-like format to be recognizable.

**Interaction contract:**

- Drag threshold prevents accidental drag from click jitter.
- Once threshold is crossed, drag starts exactly once.
- Releasing mouse outside the app ends drag and clears UI dragging state.
- Image history drag exposes at least a real existing image file path through `CF_HDROP`.
- If feasible, image drag also exposes bitmap data format for rich editors; if not feasible in this pass, document it as follow-up.
- Text card drag exposes Unicode text.
- File card drag exposes original file paths.
- Drag failure must clear `window.__isDragging` and `.card-dragging`.

**Acceptance criteria:**

- Drag text card into a text editor: text is inserted or accepted by the target.
- Drag image card into QQ/Codex input that accepts images: image is accepted as an upload/drop.
- Drag file card into Explorer or editable target: file path/drop is accepted.
- Failed/cancelled drag does not leave the app stuck in dragging state.
- After any drag attempt, the next single click selects normally.
- `check-interaction-affordance.ps1` verifies drag cleanup paths and no long unconditional shield remains after failure.

**Manual verification matrix:**

- Text card -> Notepad or Codex input.
- Image card -> QQ chat input.
- Image card -> Codex input if it supports dropped images.
- File card -> Explorer folder or file-accepting target.
- Cancel drag with Escape; then click a card.

---

## Task FB05: Add Safe Window Move Handle

**Goal:** The summoned clipboard window should be movable without breaking list selection, preview text selection, or card drag/drop.

**Affected files:**

- Modify: `clip/clip/wwwroot/index.html`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `clip/clip/wwwroot/js/bridge.js` if using a JS-to-native bridge action.
- Modify: `clip/clip/wwwroot/js/drag.js` or create `clip/clip/wwwroot/js/windowMove.js`
- Modify: `clip/clip/Bridge/WebBridge.cs`
- Modify: `clip/clip/Bridge/WindowActions.cs`
- Modify: `clip/clip/MainWindow.xaml.cs`

**Preferred design:**

- Add a small top drag handle or use the empty area around the nav bar as move handle.
- Do not mark the full app as draggable.
- Native side should own actual window movement.
- Reuse existing `MainWindow` window movement helpers if possible; current `DragRegion_PointerPressed/Moved/Released` exists but needs a real reachable UI region or bridge path.

**Interaction contract:**

- User can drag the window by a visible or discoverable handle.
- Dragging the handle moves the window smoothly.
- Dragging list cards still starts card drag/drop, not window move.
- Dragging inside preview text selects text, not window move.
- Dragging search/settings/vault controls does not move the window.

**Acceptance criteria:**

- Window can be moved after summon.
- Moved position remains only for the current showing session unless persistence is explicitly added later.
- Card drag and window drag do not conflict.
- The handle is small, visually quiet, and does not consume meaningful vertical space.

**Manual verification:**

- Summon window, drag the handle to another screen position.
- Click cards after moving.
- Drag a card after moving.
- Open preview and select text after moving.

---

## Task FB06: Make `?` Help Reliable

**Goal:** Replace unreliable keyboard-only help with a dependable operation tips entry in Settings.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/settings.js`
- Modify: `clip/clip/wwwroot/js/overlays.js`
- Modify: `clip/clip/wwwroot/index.html`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `clip/clip/wwwroot/locales/zh.json`
- Modify: `clip/clip/wwwroot/locales/en.json`
- Modify: `scripts/check-interaction-affordance.ps1`

**Decision update:**

- Runtime feedback says `?` still does not respond.
- Do not spend more time making `?` the primary entry.
- Keep `?` / `Shift+/` as optional secondary shortcuts only if they are already cheap and non-invasive.
- Primary help must be a visible Settings entry named 操作提示 / Operation Tips.

**Interaction contract:**

- Add a Settings panel entry for 操作提示 / Operation Tips.
- Clicking the Settings entry opens a compact tips modal or panel.
- Tips content must list only actual supported interactions:
  - Single click: select.
  - Double-click: paste after FB01 is fixed.
  - Enter: paste selected.
  - Space or preview button: preview.
  - 1-9: quick paste.
  - Ctrl+F: search.
  - Escape: close top layer or hide window.
  - Drag card: drag content to another app after FB04 is fixed.
- The tips entry must not crowd the main clipboard list.
- Escape closes the tips modal before closing settings/window.
- If `?` remains implemented, it must be treated as optional and must not be required for acceptance.

**Acceptance criteria:**

- Open Settings, click 操作提示 / Operation Tips: tips surface opens.
- Press Escape while tips surface is open: tips closes, settings remains open if it was underneath.
- Tips text is localized in zh/en.
- No visible tutorial text is added to the main list surface.
- `check-interaction-affordance.ps1` verifies:
  - Settings contains an operation tips entry.
  - Tips surface exists.
  - Tips content includes Enter, Space, 1-9, Ctrl+F, Escape, and double-click.

**Manual verification:**

- Summon window, open Settings, click 操作提示.
- Press Escape: tips closes.
- Press Escape again: settings closes or window hides according to existing top-layer rules.
- Switch language if available and confirm tips text follows locale.

## Suggested Execution Order

1. FB02: Make paste close immediate. Already reported complete; only regression-test it.
2. FB01: Restore double-click paste using click-timing or non-rebuilding selection logic.
3. FB06: Add Settings operation tips entry; treat `?` as optional.
4. FB03: Improve text preview exit and selection.
5. FB05: Add safe window move handle.
6. FB04: Fix native drag reliability and image drag.

## Required Verification Before Handoff

- `powershell -ExecutionPolicy Bypass -File scripts\check-frontend-js.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts\check-interaction-affordance.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts\verify-build.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts\check-runtime-smoke.ps1`

Manual smoke:

- Double-click text/image/file cards.
- Enter paste, 1-9 paste, paste button.
- Text preview select/copy/right-click/Escape.
- Drag image into QQ/Codex target.
- Move window by handle.
- Open Settings -> Operation Tips.
