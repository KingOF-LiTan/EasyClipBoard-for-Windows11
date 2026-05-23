using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using clip.Core.Models;
using clip.Core.Storage;

namespace clip.Bridge;

internal sealed class MediaActions
{
    private readonly StorageService _storage;

    public MediaActions(StorageService storage)
    {
        _storage = storage;
    }

    public async Task<object> GetImageThumbnailAsync(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idProp)) return BridgeResponse.Fail("missingId");
        long id = idProp.GetInt64();
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null) return BridgeResponse.Fail("itemNotFound");

        string? fullPath = _storage.GetBlobFullPath(entity.ImageBlobPath);
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return BridgeResponse.Fail("imageNotFound");

        try
        {
            byte[] imgBytes = File.ReadAllBytes(fullPath);
            string ext = Path.GetExtension(fullPath).TrimStart('.').ToLower();
            if (ext == "jpg") ext = "jpeg";
            if (string.IsNullOrEmpty(ext)) ext = "png";
            string base64 = $"data:image/{ext};base64,{Convert.ToBase64String(imgBytes)}";
            return new { success = true, id, base64 };
        }
        catch (Exception ex)
        {
            return BridgeResponse.Fail(ex.Message);
        }
    }

    public async Task<object> ShowImagePreviewWindowAsync(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idProp)) return BridgeResponse.Fail("missingId");
        long id = idProp.GetInt64();
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null || entity.Type != ClipboardItemType.Image || string.IsNullOrEmpty(entity.ImageBlobPath))
            return BridgeResponse.Fail("imageNotFound");

        string? fullPath = _storage.GetBlobFullPath(entity.ImageBlobPath);
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return BridgeResponse.Fail("imageNotFound");

        MainWindow.Current?.DispatcherQueue.TryEnqueue(() =>
        {
            new ImagePreviewWindow(id, fullPath, entity.ImageWidth ?? 0, entity.ImageHeight ?? 0);
        });

        return new { success = true };
    }

    public async Task<object> GetFullTextAsync(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idProp)) return BridgeResponse.Fail("missingId");
        long id = idProp.GetInt64();
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null) return BridgeResponse.Fail("itemNotFound");

        if (entity.IsSensitive)
        {
            return new { success = true, text = "********" };
        }

        if (entity.Type == ClipboardItemType.Text)
        {
            return new { success = true, text = entity.TextContent ?? "" };
        }

        if (entity.Type == ClipboardItemType.Files && !string.IsNullOrEmpty(entity.FilePathsJson))
        {
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(entity.FilePathsJson);
                if (paths is { Count: > 0 })
                    return new { success = true, text = string.Join("\n", paths) };
            }
            catch { return new { success = true, text = "[文件]" }; }
        }

        return BridgeResponse.Fail("unsupportedType");
    }
}
