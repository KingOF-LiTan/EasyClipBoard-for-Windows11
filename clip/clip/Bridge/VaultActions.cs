using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using clip.Core.Clipboard;
using clip.Core.Models;
using clip.Core.Security;
using clip.Core.Storage;

namespace clip.Bridge;

internal sealed class VaultActions
{
    private readonly StorageService _storage;
    private readonly ClipboardItemDtoMapper _mapper;
    private readonly Action _onBeforeClipboardWrite;

    public VaultActions(StorageService storage, ClipboardItemDtoMapper mapper, Action onBeforeClipboardWrite)
    {
        _storage = storage;
        _mapper = mapper;
        _onBeforeClipboardWrite = onBeforeClipboardWrite;
    }

    public async Task<object> GetSensitiveItemsAsync(JsonElement root)
    {
        string? search = root.TryGetProperty("search", out var s) ? s.GetString() : null;
        var entities = await _storage.GetSensitiveItemsAsync(search);
        return entities.Select(_mapper.Map).ToList();
    }

    public async Task<object> PasteTextAsync(JsonElement root)
    {
        string text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(text)) return BridgeResponse.Fail("emptyText");
        _onBeforeClipboardWrite();
        await ClipboardWriter.WriteTextAsync(text);
        return new { success = true };
    }

    public async Task<object> AddSecretAsync(JsonElement root)
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

    public async Task<object> DeleteSecretAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        await _storage.DeleteItemAsync(id);
        return new { success = true };
    }

    public async Task<object> DecryptSecretAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null || !entity.IsSensitive) return new { text = entity?.TextContent };
        return new { text = EncryptionService.Decrypt(entity.TextContent ?? "") };
    }

    public async Task<object> GetUsernameAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null) return BridgeResponse.Fail("itemNotFound");
        return new { success = true, text = entity.Username ?? "" };
    }

    public async Task<object> UpdateAliasAsync(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        string? alias = root.TryGetProperty("alias", out var a) ? a.GetString() : null;
        await _storage.UpdateItemAliasAsync(id, alias);
        return new { success = true };
    }
}
