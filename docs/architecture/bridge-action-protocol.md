# WinClipboard Bridge Action Protocol

> Auto-generated from code in `clip/clip/Bridge/` and `clip/clip/wwwroot/js/`.
> Update this document whenever an action signature, response shape, or ownership changes.

## Protocol Overview

All bridge communication follows this pattern:

- **Request**: `window.chrome.webview.postMessage({ action, requestId, ...params })`
- **Response**: C# executes `window.__bridge_response({ requestId, data })`
- **Timeout**: 5 seconds (except `selectBackgroundImage` which has no timeout)
- **Error convention**: `{ success: false, error: "<message>" }` using `BridgeResponse.Fail()`.
  Any unhandled C# exception in `WebBridge.HandleMessageAsync()` also produces this shape. Per-action explicit errors are listed below.
- **Success convention**: varies by action (see table).
- **Frontend convention**: `bridge.js` returns `{ success: false, error }` for WebView2 unavailable and timeout cases, and logs structured bridge failures while preserving raw-list compatibility for list actions.

### DTO Shape (from `ClipboardItemDtoMapper`)

Every items-list action returns objects with these fields:
`id`, `type` (text/image/files), `tag` (Important/Frequent/Script/Temporary), `preview`, `colorHex`, `alias`,
`isFavorite`, `isSensitive`, `sensitiveType`, `username`, `remark`,
`capturedAt`, `timeAgo`, `hasImage`, `imageWidth`, `imageHeight`.

Sensitive items have `preview` masked and `text_content` excluded by the mapper.

---

## History & Favorites

| Action | Owner | Request | Success | Failure | Suppress | Sensitive | Caller |
|---|---|---|---|---|---|---|---|
| `getHistory` | `HistoryActions` | `{ search?: string, limit?: number }` | `ClipboardItemDto[]` (array) | N/A (always returns array; global exception handler may send error) | No | Masked preview | `history.js` |
| `getFavorites` | `HistoryActions` | `{ search?: string, category?: string }` | `ClipboardItemDto[]` (filtered by category if specified) | N/A | No | Masked preview | `history.js` |
| `paste` | `HistoryActions` | `{ id: number }` | `{ success: true }` | `{ success: false, error: "itemNotFound" }` | **Yes** | Does NOT decrypt sensitive text; vault items must use `decryptSecret` + `pasteText` | `listActions.js`, `keyboard.js` |
| `delete` | `HistoryActions` | `{ id: number }` | `{ success: true }` | N/A (no action-level error) | No | No plaintext returned | `listActions.js` |
| `toggleFavorite` | `HistoryActions` | `{ id: number }` | `{ success: true }` | N/A | No | No plaintext returned | `listActions.js` |
| `updateTag` | `HistoryActions` | `{ id: number, tag: string }` | `{ success: true }` | N/A (invalid tag falls back to `Temporary`) | No | No plaintext returned | `listActions.js` |
| `clearHistory` | `HistoryActions` | `{}` | `{ success: true }` | N/A | No | Vault items are NOT affected | `history.js` |

## Vault (Sensitive Items)

| Action | Owner | Request | Success | Failure | Suppress | Sensitive | Caller |
|---|---|---|---|---|---|---|---|
| `getSensitiveItems` | `VaultActions` | `{ search?: string }` | `ClipboardItemDto[]` (sensitive-only) | N/A | No | Masked preview; encrypted content not returned | `vault.js` |
| `addSecret` | `VaultActions` | `{ alias: string, content: string, sensitiveType: string, username?: string, remark?: string }` | `{ success: true }` | N/A (throws on missing required fields → caught by global handler) | No clipboard write | Content encrypted before storage | `vault.js` |
| `deleteSecret` | `VaultActions` | `{ id: number }` | `{ success: true }` | N/A | No | No plaintext returned | `vault.js` |
| `decryptSecret` | `VaultActions` | `{ id: number }` | `{ text: string }` — decrypted plaintext | Returns `{ text: null }` if item not found or not sensitive | No direct write | **Returns plaintext** — caller must only hold in memory and call `pasteText` | `vault.js` |
| `getUsername` | `VaultActions` | `{ id: number }` | `{ success: true, text: string }` | `{ success: false, error: "itemNotFound" }` | No direct write | Username stored as plaintext; not secret content | `vault.js` |
| `pasteText` | `VaultActions` | `{ text: string }` | `{ success: true }` | `{ success: false, error: "emptyText" }` | **Yes** | Writes decrypted text to clipboard — only used after explicit decrypt | `vault.js` |
| `updateAlias` | `VaultActions` | `{ id: number, alias?: string }` | `{ success: true }` | N/A | No | No plaintext returned | None (reserved) |

## Settings

| Action | Owner | Request | Success | Failure | Suppress | Sensitive | Caller |
|---|---|---|---|---|---|---|---|
| `getSettings` | `SettingsActions` | `{}` | `{ theme, bgBase64, maskOpacity, blurAmount, autostart, language, modifiers, vk, code }` | N/A (global exception handler) | No | No | `settings.js` |
| `setTheme` | `SettingsActions` | `{ theme: number }` — 1=dark, 2=light | `{ success: true, theme: number }` | N/A | No | No | `settings.js` |
| `setLanguage` | `SettingsActions` | `{ language: string }` — `"zh"` or `"en"` | `{ success: true }` + pushes locale JSON to JS | N/A | No | No | `settings.js` |
| `selectBackgroundImage` | `WindowActions` | `{}` — opens native file picker | `{ success: true, base64: string }` | `{ success: false, error: "cancelled" }` or exception message | No | No | `settings.js` |
| `setBackground` | `SettingsActions` | `{ path: string }` | `{ success: true }` | N/A | No | No | None (internal; path set by `selectBackgroundImage`) |
| `clearBackground` | `SettingsActions` | `{}` | `{ success: true }` | N/A | No | No | `settings.js` |
| `setMaskOpacity` | `SettingsActions` | `{ opacity: number }` | `{ success: true }` | N/A | No | No | `settings.js` |
| `getAutostart` | `SettingsActions` | `{}` | `{ enabled: boolean }` | N/A | No | No | None (autostart state via `getSettings`) |
| `setAutostart` | `SettingsActions` | `{ enabled: boolean }` | `{ success: true, enabled: boolean }` | `{ success: false, error: string }` — `"registryUnavailable"` or exception | No | No | `settings.js` |
| `setShortcut` | `SettingsActions` | `{ modifiers: number, code: string }` | `{ success: true, modifiers, vk, code }` | `{ success: false, error: "shortcutError" }` | No | No | `keyboard.js` |

Modifiers bitmask: 0x0001=Alt, 0x0002=Ctrl, 0x0004=Shift, 0x0008=Win. `code` is JS `KeyboardEvent.code` (e.g. `"Tab"`, `"KeyA"`).

## Media & Preview

| Action | Owner | Request | Success | Failure | Suppress | Sensitive | Caller |
|---|---|---|---|---|---|---|---|
| `getImageThumbnail` | `MediaActions` | `{ id: number }` | `{ success: true, id: number, base64: string }` | `{ success: false, error: "missingId" \| "itemNotFound" \| "imageNotFound" \| string }` | No | Image blob only (not sensitive content) | `listView.js` |
| `getFullText` | `MediaActions` | `{ id: number }` | `{ success: true, text: string }` | `{ success: false, error: "missingId" \| "itemNotFound" \| "unsupportedType" }` | No | Sensitive items return `"********"` | `overlays.js` |
| `showImagePreviewWindow` | `MediaActions` | `{ id: number }` | `{ success: true }` — opens native preview window | `{ success: false, error: "missingId" \| "imageNotFound" }` | No | Image-only; no text content exposed | `overlays.js` |

## Window & Utility

| Action | Owner | Request | Success | Failure | Suppress | Sensitive | Caller |
|---|---|---|---|---|---|---|---|
| `hideWindow` | `WindowActions` | `{}` | `{ success: true }` | N/A | No | No | `listActions.js`, `overlays.js`, `vault.js` |
| `log` | `WindowActions` | `{ level?: string, message?: string }` | `null` (no response body sent) | N/A | No | Frontend must NOT log secret plaintext | `bridge.js` |
| `startDrag` | `WindowActions` | `{ id: number }` | `{ success: true }` | `{ success: false, error: "missingId" \| "itemNotFound" \| "emptyDragData" }` | No clipboard write | Text content used for drag data — sensitive items should not be draggable | `drag.js` |

---

## Frontend Caller Index

| Module | Actions Called |
|---|---|
| `js/bridge.js` | `log` (also hosts `__bridge_response`, `__on_clipboard_updated`) |
| `js/history.js` | `getHistory`, `getFavorites`, `clearHistory` |
| `js/listActions.js` | `paste`, `delete`, `toggleFavorite`, `updateTag`, `hideWindow` |
| `js/listView.js` | `getImageThumbnail` |
| `js/keyboard.js` | `setShortcut` |
| `js/settings.js` | `getSettings`, `setTheme`, `setLanguage`, `selectBackgroundImage`, `clearBackground`, `setMaskOpacity`, `setAutostart` |
| `js/vault.js` | `getSensitiveItems`, `addSecret`, `deleteSecret`, `decryptSecret`, `getUsername`, `pasteText`, `hideWindow` |
| `js/overlays.js` | `hideWindow`, `getFullText`, `showImagePreviewWindow` |
| `js/drag.js` | `startDrag` |

---

## Known Issues

1. **`maskOpacity` drift**: `getSettings()` hardcodes `maskOpacity = 0.3` in the response, while `setMaskOpacity` persists the actual value to `ui_background_mask_opacity`. The persisted value and the returned value can diverge.
2. **`getAutostart` / `setBackground` unused**: These two actions are defined but have no frontend caller. Autostart status is embedded in `getSettings`; `setBackground` is only called internally by C# `selectBackgroundImage` flow.
3. **`delete` / `clearHistory` / `toggleFavorite` etc. have no action-level error handling** — they return `{ success: true }` unconditionally. Errors are only caught at the global `WebBridge.HandleMessageAsync()` level.
4. **`log` action returns `null`** — this means no response at all; the frontend promise resolves to `null` due to timeout. This is by design.

---

## Verification Status

- **30 actions** in `WebBridge.cs` `HandleMessageAsync` → all documented
- All frontend `app.bridge.send(...)` calls mapped to protocol rows
- Actions without frontend callers documented: `getAutostart`, `setBackground`, `updateAlias`
