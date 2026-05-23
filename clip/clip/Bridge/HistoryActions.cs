using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using clip.Core.Clipboard;
using clip.Core.Models;
using clip.Core.Storage;

namespace clip.Bridge;

internal sealed class HistoryActions
{
    private readonly StorageService _storage;
    private readonly ClipboardItemDtoMapper _mapper;
    private readonly Action _onBeforeClipboardWrite;

    public HistoryActions(StorageService storage, ClipboardItemDtoMapper mapper, Action onBeforeClipboardWrite)
    {
        _storage = storage;
        _mapper = mapper;
        _onBeforeClipboardWrite = onBeforeClipboardWrite;
    }

    public async Task<object> GetHistoryAsync(JsonElement root)
    {
        string search = root.TryGetProperty("search", out var s) ? s.GetString() ?? "" : "";
        int limit = root.TryGetProperty("limit", out var l) ? l.GetInt32() : 200;
        var entities = await _storage.GetHistoryAsync(limit, search);
        return entities.Select(_mapper.Map).ToList();
    }

    public async Task<object> GetFavoritesAsync(JsonElement root)
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
        return filtered.Select(_mapper.Map).ToList();
    }

    public async Task<object> PasteAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null) return BridgeResponse.Fail("itemNotFound");
        _onBeforeClipboardWrite();
        await ClipboardWriter.WriteAsync(entity, _storage);
        return new { success = true };
    }

    public async Task<object> DeleteAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        await _storage.DeleteItemAsync(id);
        return new { success = true };
    }

    public async Task<object> ToggleFavoriteAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        await _storage.ToggleFavoriteAsync(id);
        return new { success = true };
    }

    public async Task<object> UpdateTagAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        string tagStr = root.GetProperty("tag").GetString() ?? "Temporary";
        var tag = Enum.TryParse<ClipboardItemTag>(tagStr, out var t) ? t : ClipboardItemTag.Temporary;
        await _storage.UpdateItemTagAsync(id, tag);
        return new { success = true };
    }

    public async Task<object> ClearHistoryAsync()
    {
        await _storage.ClearHistoryAsync();
        return new { success = true };
    }
}
