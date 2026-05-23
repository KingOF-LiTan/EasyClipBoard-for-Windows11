# WinClipboard Interaction Experience Task Cards

> Role: Architect
>
> Goal: make the WebView2 clipboard window feel smoother and more predictable after the boundary split, without changing the core clipboard pipeline.
>
> Execution owner: Implementer. Architect only defines task cards, boundaries, and acceptance criteria.

## Current Interaction Baseline

- Card semantics are now: single click selects; double-click or explicit paste button pastes; Enter pastes selected; Space previews; 1-9 quick-pastes.
- The frontend is split into `wwwroot/js/*`; keep this boundary.
- Do not reintroduce inline `onclick`, `onchange`, `oninput`, or `.onclick`.
- Runtime safety checks exist:
  - `scripts/verify-build.ps1`
  - `scripts/check-frontend-js.ps1`
  - `scripts/check-runtime-smoke.ps1`
  - `scripts/check-interaction-affordance.ps1`
- Any interaction change must preserve drag, paste, keyboard, settings, vault, transparent background, and sensitive masking.

## Experience Problems To Solve

- Selected card state does not yet strongly guide the next action.
- Paste, preview, favorite, delete, and tag actions compete visually.
- Image cards and text cards do not have equally discoverable preview/paste affordances.
- Keyboard users have powerful shortcuts, but the UI does not surface enough lightweight feedback.
- Destructive actions need consistent confirmation and no accidental activation.
- The window is compact, so any new affordance must be dense and predictable.

---

## Task UX01: Selected Card Action Rail

**Goal:** Make the selected card clearly actionable without requiring hover.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/listView.js`
- Modify: `clip/clip/wwwroot/js/listActions.js`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `scripts/check-interaction-affordance.ps1`

**Interaction contract:**

- Single click selects only.
- Selected card always exposes primary actions.
- Paste is visually primary on the selected card.
- Hover still reveals actions for non-selected cards.

**Acceptance criteria:**

- `scripts/check-interaction-affordance.ps1` verifies:
  - `onCardClick` does not call paste.
  - `.card.selected .card-actions` exists.
  - selected card paste affordance is visibly styled.
- Mouse users can select a card and immediately see the paste affordance.
- Keyboard users still use Enter and 1-9 exactly as before.

**Verification:**

- `powershell -ExecutionPolicy Bypass -File scripts/check-interaction-affordance.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/check-frontend-js.ps1`

---

## Task UX02: Preview Affordance Unification

**Goal:** Make preview discoverable for text, image, and file cards with one consistent interaction model.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/listView.js`
- Modify: `clip/clip/wwwroot/js/overlays.js`
- Modify: `clip/clip/wwwroot/js/keyboard.js`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `clip/clip/wwwroot/locales/zh.json`
- Modify: `clip/clip/wwwroot/locales/en.json`

**Interaction contract:**

- Space previews selected item.
- A preview icon/button is available in card actions.
- Image cards use the same preview action instead of relying only on clicking the image.
- File cards preview their full path list through `getFullText`.

**Acceptance criteria:**

- Preview action exists for every card type.
- Preview does not paste.
- Image preview still opens native image preview window.
- Text/file preview still uses modal.
- Escape closes preview first.

**Verification:**

- Text card: select, Space, preview modal opens.
- Image card: preview action opens native preview.
- File card: preview action shows file paths.
- `powershell -ExecutionPolicy Bypass -File scripts/check-frontend-js.ps1`

---

## Task UX03: Confirm Destructive Actions

**Goal:** Prevent accidental destructive operations.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/listActions.js`
- Modify: `clip/clip/wwwroot/js/history.js`
- Modify: `clip/clip/wwwroot/js/vault.js`
- Modify: `clip/clip/wwwroot/js/overlays.js`
- Modify: `clip/clip/wwwroot/locales/zh.json`
- Modify: `clip/clip/wwwroot/locales/en.json`

**Interaction contract:**

- Delete card requires confirm.
- Delete vault secret requires confirm.
- Clear history already requires confirm and must remain unchanged.
- Confirm modal copy must state the object being deleted when practical.

**Acceptance criteria:**

- Delete card opens confirm modal before bridge `delete`.
- Delete secret opens confirm modal before bridge `deleteSecret`.
- Escape closes confirm modal without deleting.
- Confirm OK performs exactly one deletion.

**Verification:**

- Try deleting card, press Escape: item remains.
- Try deleting card, confirm: item disappears.
- Try deleting vault item, cancel: item remains.
- Try clearing history: existing behavior remains.

---

## Task UX04: Lightweight Status Feedback

**Goal:** Give immediate feedback for paste, favorite, tag, delete, and vault copy actions without modal noise.

**Affected files:**

- Create or modify: `clip/clip/wwwroot/js/toast.js`
- Modify: `clip/clip/wwwroot/app.js`
- Modify: `clip/clip/wwwroot/index.html`
- Modify: `clip/clip/wwwroot/js/listActions.js`
- Modify: `clip/clip/wwwroot/js/vault.js`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `clip/clip/wwwroot/locales/zh.json`
- Modify: `clip/clip/wwwroot/locales/en.json`

**Interaction contract:**

- Toast is compact and auto-dismisses.
- It must not block keyboard input.
- It must not appear over the selected card action area.
- Sensitive copy feedback must not include plaintext.

**Acceptance criteria:**

- Paste shows "已复制到剪贴板" / "Copied".
- Favorite toggle shows pinned/unpinned feedback.
- Delete shows deleted feedback after success.
- Vault copy shows copied feedback without revealing secret.
- Toast disappears automatically.

**Verification:**

- Trigger each action and confirm toast appears.
- Confirm no sensitive plaintext appears in DOM feedback.
- Confirm Escape behavior is not affected.

---

## Task UX05: Search And Filter Polish

**Goal:** Make `/img` and `/file` filters more discoverable and less typo-prone.

**Affected files:**

- Modify: `clip/clip/wwwroot/index.html`
- Modify: `clip/clip/wwwroot/js/history.js`
- Modify: `clip/clip/wwwroot/js/keyboard.js`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `clip/clip/wwwroot/locales/zh.json`
- Modify: `clip/clip/wwwroot/locales/en.json`

**Interaction contract:**

- Keep text search as the default.
- Keep `/img` and `/file` input shortcuts.
- Add small filter chips or segmented mini-controls for All/Text/Image/File if space allows.
- Filter controls must not steal 1-9 quick paste.

**Acceptance criteria:**

- User can filter image/file history without typing slash commands.
- Slash commands still work.
- Search input remains focused after changing filter if it was focused before.
- Empty state reflects active filter.

**Verification:**

- Type `/img`: image filter applies.
- Click Image filter: same result.
- Switch tabs: filter behavior is predictable and documented in `system_map.md`.

---

## Task UX06: Keyboard Help Surface

**Goal:** Surface the existing keyboard model without adding a tutorial screen.

**Affected files:**

- Modify: `clip/clip/wwwroot/index.html`
- Modify: `clip/clip/wwwroot/js/keyboard.js`
- Modify: `clip/clip/wwwroot/js/overlays.js`
- Modify: `clip/clip/wwwroot/styles.css`
- Modify: `clip/clip/wwwroot/locales/zh.json`
- Modify: `clip/clip/wwwroot/locales/en.json`

**Interaction contract:**

- Add a compact help affordance, not a large guide.
- Help can be a small panel/modal reachable by `?` or a small icon.
- It lists only actual shortcuts:
  - ArrowUp/ArrowDown: select
  - Enter: paste
  - Space: preview
  - 1-9: quick paste
  - Ctrl+F: search
  - Escape: close/hide

**Acceptance criteria:**

- Help opens and closes with Escape.
- Help does not interfere with shortcut recorder.
- Help content is localized.
- No visible instructional text is added to the main list surface.

**Verification:**

- Press `?` with no input focused: help opens.
- Press Escape: help closes.
- Focus search input and type `?`: input receives `?`, help does not open.

---

## Task UX07: Drag Confidence Pass

**Goal:** Make drag feel deliberate and prevent accidental paste/selection weirdness.

**Affected files:**

- Modify: `clip/clip/wwwroot/js/drag.js`
- Modify: `clip/clip/wwwroot/js/listView.js`
- Modify: `clip/clip/wwwroot/styles.css`

**Interaction contract:**

- Drag threshold remains high enough to avoid accidental drag.
- Drag start gives visible state.
- Drag completion clears visual state reliably.
- Drag never triggers paste.

**Acceptance criteria:**

- Pointer jitter under threshold selects card only.
- Drag over threshold starts native drag.
- After drag ends/cancels, card opacity/state resets.
- Anti-click shield duration is documented in `drag.js`.

**Verification:**

- Mouse click selected card: no drag, no paste.
- Drag text card: native drag starts.
- Drag image/file card: native drag starts.
- After failed/cancelled drag, next click works normally.

---

## Task UX08: Interaction Smoke Script

**Goal:** Keep interaction semantics from regressing.

**Affected files:**

- Modify: `scripts/check-interaction-affordance.ps1`
- Optional create: `scripts/check-interaction-static.ps1`

**Static checks to include:**

- No inline event handlers.
- `onCardClick` does not paste.
- selected card reveals actions.
- preview action exists.
- destructive actions call confirm before bridge delete.
- shortcut help only opens outside inputs if implemented.

**Acceptance criteria:**

- Script exits nonzero when a core interaction contract regresses.
- Script remains static and fast; it should not require launching the app.

**Verification:**

- `powershell -ExecutionPolicy Bypass -File scripts/check-interaction-affordance.ps1`

## Suggested Execution Order

1. UX01: Selected Card Action Rail
2. UX02: Preview Affordance Unification
3. UX03: Confirm Destructive Actions
4. UX04: Lightweight Status Feedback
5. UX05: Search And Filter Polish
6. UX06: Keyboard Help Surface
7. UX07: Drag Confidence Pass
8. UX08: Interaction Smoke Script

## Required Verification Before Handoff

- `powershell -ExecutionPolicy Bypass -File scripts/verify-build.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/check-frontend-js.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/check-runtime-smoke.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/check-interaction-affordance.ps1`
- Manual check: select, paste, preview, delete confirm, favorite, tag, drag, Escape.
