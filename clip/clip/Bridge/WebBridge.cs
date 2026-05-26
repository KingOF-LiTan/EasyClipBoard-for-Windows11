using System;
using System.Text.Json;
using System.Threading.Tasks;
using clip.Bridge;
using clip.Core.Storage;

namespace clip;

/// <summary>
/// Handles JSON messages from the WebView2 frontend and routes them to focused action groups.
/// </summary>
public sealed class WebBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly Func<string, Task> _sendToJs;
    private readonly HistoryActions _historyActions;
    private readonly VaultActions _vaultActions;
    private readonly SettingsActions _settingsActions;
    private readonly MediaActions _mediaActions;
    private readonly WindowActions _windowActions;

    public WebBridge(StorageService storage, Action onBeforeClipboardWrite, Func<string, Task> sendToJs)
    {
        _sendToJs = sendToJs;

        var mapper = new ClipboardItemDtoMapper(storage);
        _historyActions = new HistoryActions(storage, mapper, onBeforeClipboardWrite);
        _vaultActions = new VaultActions(storage, mapper, onBeforeClipboardWrite);
        _settingsActions = new SettingsActions(sendToJs);
        _mediaActions = new MediaActions(storage);
        _windowActions = new WindowActions(storage);
    }

    public async Task HandleMessageAsync(string jsonMessage)
    {
        try
        {
            using var request = BridgeRequest.Parse(jsonMessage);

            object? result = request.Action switch
            {
                "getHistory" => await _historyActions.GetHistoryAsync(request.Root),
                "getFavorites" => await _historyActions.GetFavoritesAsync(request.Root),
                "paste" => await _historyActions.PasteAsync(request.Root),
                "delete" => await _historyActions.DeleteAsync(request.Root),
                "toggleFavorite" => await _historyActions.ToggleFavoriteAsync(request.Root),
                "updateTag" => await _historyActions.UpdateTagAsync(request.Root),
                "clearHistory" => await _historyActions.ClearHistoryAsync(),
                "getSensitiveItems" => await _vaultActions.GetSensitiveItemsAsync(request.Root),
                "pasteText" => await _vaultActions.PasteTextAsync(request.Root),
                "addSecret" => await _vaultActions.AddSecretAsync(request.Root),
                "deleteSecret" => await _vaultActions.DeleteSecretAsync(request.Root),
                "decryptSecret" => await _vaultActions.DecryptSecretAsync(request.Root),
                "getUsername" => await _vaultActions.GetUsernameAsync(request.Root),
                "updateAlias" => await _vaultActions.UpdateAliasAsync(request.Root),
                "getSettings" => _settingsActions.GetSettings(),
                "setBackground" => _settingsActions.SetBackground(request.Root),
                "clearBackground" => _settingsActions.ClearBackground(),
                "setTheme" => _settingsActions.SetTheme(request.Root),
                "setShortcut" => _settingsActions.SetShortcut(request.Root),
                "setLanguage" => await _settingsActions.SetLanguage(request.Root),
                "getAutostart" => _settingsActions.GetAutostart(),
                "setAutostart" => _settingsActions.SetAutostart(request.Root),
                "setMaskOpacity" => _settingsActions.SetMaskOpacity(request.Root),
                "getImageThumbnail" => await _mediaActions.GetImageThumbnailAsync(request.Root),
                "getFullText" => await _mediaActions.GetFullTextAsync(request.Root),
                "showImagePreviewWindow" => await _mediaActions.ShowImagePreviewWindowAsync(request.Root),
                "hideWindow" => _windowActions.HideWindowImmediate(),
                "selectBackgroundImage" => await _windowActions.SelectBackgroundImageAsync(),
                "log" => _windowActions.LogFromJs(request.Root),
                "startDrag" => await _windowActions.StartDragAsync(request.Root),
                "startWindowMove" => _windowActions.StartWindowMove(),
                _ => BridgeResponse.Fail($"Unknown action: {request.Action}")
            };

            if (request.RequestId != null && result != null)
            {
                await SendResponseAsync(request.RequestId, result);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBridge] Error: {ex.Message}");
            var requestId = BridgeRequest.TryGetRequestId(jsonMessage);
            if (requestId != null)
            {
                await SendResponseAsync(requestId, BridgeResponse.Fail(ex.Message));
            }
        }
    }

    public async Task PushClipboardUpdateAsync()
    {
        await _sendToJs("window.__on_clipboard_updated && window.__on_clipboard_updated()");
    }

    private async Task SendResponseAsync(string requestId, object result)
    {
        var response = new { requestId, data = result };
        string json = JsonSerializer.Serialize(response, JsonOptions);
        await _sendToJs($"window.__bridge_response({json})");
    }
}
