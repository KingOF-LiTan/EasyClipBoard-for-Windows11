using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using clip.Core.Clipboard;
using clip.Core.Models;
using clip.Core.Security;
using clip.Core.Storage;

namespace clip;

/// <summary>
/// Handles JSON messages from the WebView2 frontend and routes them to StorageService.
/// </summary>
public sealed class WebBridge
{
    private readonly StorageService _storage;
    private readonly Action _onBeforeClipboardWrite;
    private readonly Func<string, Task> _sendToJs;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebBridge(StorageService storage, Action onBeforeClipboardWrite, Func<string, Task> sendToJs)
    {
        _storage = storage;
        _onBeforeClipboardWrite = onBeforeClipboardWrite;
        _sendToJs = sendToJs;
    }

    public async Task HandleMessageAsync(string jsonMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonMessage);
            var root = doc.RootElement;
            string action = root.GetProperty("action").GetString() ?? "";
            string? requestId = root.TryGetProperty("requestId", out var rid) ? rid.GetString() : null;

            object? result = action switch
            {
                "getHistory" => await GetHistoryAsync(root),
                "getFavorites" => await GetFavoritesAsync(root),
                "paste" => await PasteAsync(root),
                "delete" => await DeleteAsync(root),
                "toggleFavorite" => await ToggleFavoriteAsync(root),
                "updateTag" => await UpdateTagAsync(root),
                "clearHistory" => await ClearHistoryAsync(),
                "getSensitiveItems" => await GetSensitiveItemsAsync(root),
                "pasteText" => await PasteTextAsync(root),
                "addSecret" => await AddSecretAsync(root),
                "deleteSecret" => await DeleteSecretAsync(root),
                "decryptSecret" => await DecryptSecretAsync(root),
                "getSettings" => GetSettings(),
                "setBackground" => SetBackground(root),
                "clearBackground" => ClearBackground(),
                "setTheme" => SetTheme(root),
                "updateAlias" => await UpdateAliasAsync(root),
                "hideWindow" => HideWindowImmediate(),
                "selectBackgroundImage" => await SelectBackgroundImageAsync(),
                "getImageThumbnail" => await GetImageThumbnailAsync(root),
                "getAutostart" => GetAutostart(),
                "setAutostart" => SetAutostart(root),
                "setMaskOpacity" => SetMaskOpacity(root),
                "log" => LogFromJs(root),
                "startDrag" => StartDrag(),
                _ => new { error = $"Unknown action: {action}" }
            };

            if (requestId != null && result != null)
            {
                var response = new { requestId, data = result };
                string json = JsonSerializer.Serialize(response, _jsonOpts);
                await _sendToJs($"window.__bridge_response({json})");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBridge] Error: {ex.Message}");
        }
    }

    private async Task<object> GetHistoryAsync(JsonElement root)
    {
        string search = root.TryGetProperty("search", out var s) ? s.GetString() ?? "" : "";
        int limit = root.TryGetProperty("limit", out var l) ? l.GetInt32() : 200;
        var entities = await _storage.GetHistoryAsync(limit, search);
        return entities.Select(MapEntity).ToList();
    }

    private async Task<object> GetFavoritesAsync(JsonElement root)
    {
        string search = root.TryGetProperty("search", out var s) ? s.GetString() ?? "" : "";
        string category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "All" : "All";
        var all = await _storage.GetFavoritesAsync(search);
        var filtered = category switch
        {
            "Important" => all.Where(e => e.Tag == ClipboardItemTag.Important).ToList(),
            "Frequent" => all.Where(e => e.Tag == ClipboardItemTag.Frequent).ToList(),
            "Sensitive" => all.Where(e => e.IsSensitive).ToList(),
            _ => all
        };
        return filtered.Select(MapEntity).ToList();
    }

    private async Task<object> PasteAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null) return new { success = false };
        _onBeforeClipboardWrite();
        await ClipboardWriter.WriteAsync(entity, _storage);
        return new { success = true };
    }

    private async Task<object> PasteTextAsync(JsonElement root)
    {
        string text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(text)) return new { success = false };
        _onBeforeClipboardWrite();
        await ClipboardWriter.WriteTextAsync(text);
        return new { success = true };
    }

    private async Task<object> DeleteAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        await _storage.DeleteItemAsync(id);
        return new { success = true };
    }

    private async Task<object> ToggleFavoriteAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        await _storage.ToggleFavoriteAsync(id);
        return new { success = true };
    }

    private async Task<object> UpdateTagAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        string tagStr = root.GetProperty("tag").GetString() ?? "Temporary";
        var tag = Enum.TryParse<ClipboardItemTag>(tagStr, out var t) ? t : ClipboardItemTag.Temporary;
        await _storage.UpdateItemTagAsync(id, tag);
        return new { success = true };
    }

    private async Task<object> ClearHistoryAsync()
    {
        await _storage.ClearHistoryAsync();
        return new { success = true };
    }

    private async Task<object> GetSensitiveItemsAsync(JsonElement root)
    {
        string? search = root.TryGetProperty("search", out var s) ? s.GetString() : null;
        var entities = await _storage.GetSensitiveItemsAsync(search);
        return entities.Select(MapEntity).ToList();
    }

    private async Task<object> AddSecretAsync(JsonElement root)
    {
        string alias = root.GetProperty("alias").GetString() ?? "";
        string content = root.GetProperty("content").GetString() ?? "";
        string typeStr = root.GetProperty("sensitiveType").GetString() ?? "Password";
        string? username = root.TryGetProperty("username", out var u) ? u.GetString() : null;
        string? remark = root.TryGetProperty("remark", out var r) ? r.GetString() : null;
        var stype = Enum.TryParse<SensitiveType>(typeStr, out var st) ? st : SensitiveType.Password;
        await _storage.AddManualSecretAsync(alias, content, stype, username, remark);
        return new { success = true };
    }

    private async Task<object> DeleteSecretAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        await _storage.DeleteItemAsync(id);
        return new { success = true };
    }

    private async Task<object> DecryptSecretAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null || !entity.IsSensitive) return new { text = entity?.TextContent };
        return new { text = EncryptionService.Decrypt(entity.TextContent ?? "") };
    }

    private async Task<object> UpdateAliasAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        string? alias = root.TryGetProperty("alias", out var a) ? a.GetString() : null;
        await _storage.UpdateItemAliasAsync(id, alias);
        return new { success = true };
    }

    private object GetSettings()
    {
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
        string? bgPath = settings.Values["ui_background_path"] as string;
        string? bgBase64 = null;
        
        if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(bgPath);
                string ext = Path.GetExtension(bgPath).TrimStart('.').ToLower();
                if (ext == "jpg") ext = "jpeg";
                bgBase64 = $"data:image/{ext};base64,{Convert.ToBase64String(bytes)}";
            }
            catch { }
        }

        return new
        {
            theme = settings.Values["theme"] as int? ?? 0,
            bgBase64 = bgBase64,
            maskOpacity = 0.3, // Hardcoded per user request
            blurAmount = 30.0, // Hardcoded per user request
            autostart = GetAutostartEnabled(),
        };
    }

    private static bool GetAutostartEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("WinClipboard") != null;
        }
        catch { return false; }
    }

    private object GetAutostart()
    {
        return new { enabled = GetAutostartEnabled() };
    }

    private object SetAutostart(JsonElement root)
    {
        bool enable = root.TryGetProperty("enabled", out var en) && en.GetBoolean();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return new { success = false };
            if (enable)
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                key.SetValue("WinClipboard", exePath);
            }
            else
            {
                key.DeleteValue("WinClipboard", false);
            }
            return new { success = true, enabled = enable };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private object SetMaskOpacity(JsonElement root)
    {
        double opacity = root.TryGetProperty("opacity", out var op) ? op.GetDouble() : 0.7;
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
        settings.Values["ui_background_mask_opacity"] = opacity;
        return new { success = true };
    }

    private object SetBackground(JsonElement root)
    {
        string? path = root.TryGetProperty("path", out var p) ? p.GetString() : null;
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
        settings.Values["ui_background_path"] = path;
        return new { success = true };
    }

    private object ClearBackground()
    {
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
        settings.Values["ui_background_path"] = null;
        return new { success = true };
    }

    private object SetTheme(JsonElement root)
    {
        int theme = root.TryGetProperty("theme", out var t) ? t.GetInt32() : 0;
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
        settings.Values["theme"] = theme;
        
        // Notify MainWindow to change theme if needed
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => 
        {
            if (MainWindow.Current != null)
            {
                MainWindow.Current.ApplyTheme(theme);
            }
        });
        
        return new { success = true, theme };
    }

    private object HideWindowImmediate()
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => 
        {
            MainWindow.Current?.HideWindowImmediate();
        });
        return new { success = true };
    }

    private object HideWindow()
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => 
        {
            MainWindow.Current?.HideWindow();
        });
        return new { success = true };
    }

    private object LogFromJs(JsonElement root)
    {
        string level = root.TryGetProperty("level", out var l) ? l.GetString() ?? "info" : "info";
        string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        System.Diagnostics.Debug.WriteLine($"[WebView2 JS {level.ToUpper()}] {msg}");
        return null!;
    }

    private object StartDrag()
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
        {
            if (MainWindow.Current != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current);
                // PostMessage is non-blocking (unlike SendMessage which freezes the UI thread)
                Native.Win32Helper.ReleaseCapture();
                Native.Win32Helper.PostMessage(hwnd, 0x00A1 /*WM_NCLBUTTONDOWN*/, (IntPtr)0x0002 /*HTCAPTION*/, IntPtr.Zero);
            }
        });
        return new { success = true };
    }

    private async Task<object> SelectBackgroundImageAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            var copiedFile = await file.CopyAsync(localFolder, file.Name, Windows.Storage.NameCollisionOption.GenerateUniqueName);
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            settings.Values["ui_background_path"] = copiedFile.Path;

            byte[] bytes = File.ReadAllBytes(copiedFile.Path);
            string ext = copiedFile.FileType.TrimStart('.').ToLower();
            if (ext == "jpg") ext = "jpeg";
            string base64 = $"data:image/{ext};base64,{Convert.ToBase64String(bytes)}";

            return new { success = true, base64 = base64 };
        }
        return new { success = false };
    }

    private object MapEntity(ClipboardItemEntity e)
    {
        string preview = "";
        if (e.IsSensitive)
        {
            preview = "********";
        }
        else if (e.Type == ClipboardItemType.Text && !string.IsNullOrEmpty(e.TextContent))
        {
            var lines = e.TextContent.Split('\n');
            preview = string.Join("\n", lines.Length > 4 ? lines[..4] : lines).TrimEnd('\r', '\n');
            if (lines.Length > 4) preview += " …";
        }
        else if (e.Type == ClipboardItemType.Files && !string.IsNullOrEmpty(e.FilePathsJson))
        {
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(e.FilePathsJson);
                if (paths is { Count: > 0 })
                    preview = string.Join(", ", paths.Select(Path.GetFileName));
            }
            catch { preview = "[文件]"; }
        }
        else if (e.Type == ClipboardItemType.Image)
        {
            preview = $"[图片] {(e.ImageWidth.HasValue ? $"{e.ImageWidth}×{e.ImageHeight}" : "")}";
        }

        // Check if text looks like a hex color
        string? colorHex = null;
        if (e.Type == ClipboardItemType.Text && !string.IsNullOrEmpty(e.TextContent))
        {
            var trimmed = e.TextContent.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$"))
                colorHex = trimmed;
        }

        // Only tell JS whether there's an image; thumbnail is loaded lazily via getImageThumbnail
        bool hasImage = e.Type == ClipboardItemType.Image && !string.IsNullOrEmpty(e.ImageBlobPath)
                        && !string.IsNullOrEmpty(_storage.GetBlobFullPath(e.ImageBlobPath));

        return new
        {
            id = e.Id,
            type = e.Type.ToString().ToLower(),
            tag = e.Tag.ToString(),
            preview,
            colorHex,
            alias = e.Alias,
            isFavorite = e.IsFavorite,
            isSensitive = e.IsSensitive,
            sensitiveType = e.SensitiveType.ToString(),
            username = e.Username,
            remark = e.Remark,
            capturedAt = e.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            timeAgo = FormatTimeAgo(e.CapturedAtUtc),
            hasImage,
            imageWidth = e.ImageWidth,
            imageHeight = e.ImageHeight,
        };
    }

    private async Task<object> GetImageThumbnailAsync(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idProp)) return new { success = false };
        long id = idProp.GetInt64();
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null) return new { success = false };

        string? fullPath = _storage.GetBlobFullPath(entity.ImageBlobPath);
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return new { success = false };

        try
        {
            byte[] imgBytes = File.ReadAllBytes(fullPath);
            string ext = Path.GetExtension(fullPath).TrimStart('.').ToLower();
            if (ext == "jpg") ext = "jpeg";
            if (string.IsNullOrEmpty(ext)) ext = "png";
            string base64 = $"data:image/{ext};base64,{Convert.ToBase64String(imgBytes)}";
            return new { success = true, id, base64 };
        }
        catch
        {
            return new { success = false };
        }
    }

    private static string FormatTimeAgo(DateTimeOffset utc)
    {
        var diff = DateTimeOffset.Now - utc;
        if (diff.TotalSeconds < 60) return "刚刚";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}小时前";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}天前";
        return utc.ToLocalTime().ToString("MM/dd");
    }

    public async Task PushClipboardUpdateAsync()
    {
        await _sendToJs("window.__on_clipboard_updated && window.__on_clipboard_updated()");
    }
}
