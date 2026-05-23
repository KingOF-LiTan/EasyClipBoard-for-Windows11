using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using clip.Core.Models;

namespace clip.Core.Storage;

/// <summary>
/// CRUD for clipboard history items: save, query, search, tag, favorite, delete, purge.
/// </summary>
internal sealed class ClipboardItemRepository
{
    private readonly StorageConnection _conn;
    private readonly BlobStore _blobs;

    public ClipboardItemRepository(StorageConnection conn, BlobStore blobs)
    {
        _conn = conn;
        _blobs = blobs;
    }

    public async Task<long> SaveItemAsync(ClipboardItemDraft draft)
    {
        await _conn.Lock.WaitAsync();
        try
        {
            string? hash = null;
            string? blobRelPath = null;

            if (draft.Type == ClipboardItemType.Image && draft.ImagePng is { Length: > 0 })
            {
                hash = BlobStore.ComputeSha256(draft.ImagePng);
                var existingId = await _conn.ScalarWithLockAsync(
                    "SELECT id FROM items WHERE image_hash = @h LIMIT 1;",
                    ("@h", hash));

                if (existingId != null)
                {
                    await _conn.ExecWithLockAsync(
                        "UPDATE items SET captured_at = @t WHERE id = @id;",
                        ("@t", DateTimeOffset.UtcNow.ToString("O")),
                        ("@id", (long)existingId));
                    return (long)existingId;
                }

                blobRelPath = await _blobs.WriteBlobAsync(draft.ImagePng);
            }

            string? filePathsJson = null;
            if (draft.FilePaths is { Count: > 0 })
            {
                filePathsJson = JsonSerializer.Serialize(draft.FilePaths);
            }

            string? textToSave = draft.Text;
            bool isSensitive = draft.IsSensitive;
            if (isSensitive && !string.IsNullOrEmpty(textToSave))
            {
                textToSave = Security.EncryptionService.Encrypt(textToSave);
            }

            var sql = @"
                INSERT INTO items (type, captured_at, tag, is_sensitive, sensitive_type, text_content, username, remark, image_blob, image_hash, image_w, image_h, file_paths, alias)
                VALUES (@type, @cap, @tag, @sens, @stype, @txt, @user, @remark, @blob, @hash, @iw, @ih, @fp, @alias);
                SELECT last_insert_rowid();";

            var id = await _conn.ScalarWithLockAsync(sql,
                ("@type", (int)draft.Type),
                ("@cap", draft.CapturedAtUtc.ToString("O")),
                ("@tag", (int)draft.Tag),
                ("@sens", isSensitive ? 1 : 0),
                ("@stype", (int)draft.SensitiveType),
                ("@txt", (object?)textToSave ?? DBNull.Value),
                ("@user", (object?)draft.Username ?? DBNull.Value),
                ("@remark", (object?)draft.Remark ?? DBNull.Value),
                ("@blob", (object?)blobRelPath ?? DBNull.Value),
                ("@hash", (object?)hash ?? DBNull.Value),
                ("@iw", (object?)draft.ImageWidth ?? DBNull.Value),
                ("@ih", (object?)draft.ImageHeight ?? DBNull.Value),
                ("@fp", (object?)filePathsJson ?? DBNull.Value),
                ("@alias", DBNull.Value));

            return (long)(id ?? -1);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Storage] 保存失败: {ex.Message}");
            return -1;
        }
        finally { _conn.Lock.Release(); }
    }

    public async Task UpdateTagAsync(long id, ClipboardItemTag tag)
    {
        await _conn.ExecAsync("UPDATE items SET tag = @tag WHERE id = @id;",
            ("@tag", (int)tag), ("@id", id));
    }

    public async Task UpdateSensitiveFlagAsync(long id, bool isSensitive)
    {
        if (isSensitive)
        {
            var item = await GetByIdAsync(id);
            if (item != null && !item.IsSensitive && !string.IsNullOrEmpty(item.TextContent))
            {
                var encrypted = Security.EncryptionService.Encrypt(item.TextContent);
                await _conn.ExecAsync(
                    "UPDATE items SET is_sensitive = 1, text_content = @txt WHERE id = @id;",
                    ("@txt", encrypted), ("@id", id));
                return;
            }
        }
        await _conn.ExecAsync("UPDATE items SET is_sensitive = @s WHERE id = @id;",
            ("@s", isSensitive ? 1 : 0), ("@id", id));
    }

    public async Task UpdateOcrTextAsync(long id, string ocrText)
    {
        await _conn.ExecAsync("UPDATE items SET ocr_text = @ocr WHERE id = @id;",
            ("@ocr", ocrText), ("@id", id));
    }

    public async Task<List<ClipboardItemEntity>> GetHistoryAsync(int limit = 100, string? search = null)
    {
        string sql;
        var parameters = new List<(string, object?)>();

        if (string.IsNullOrWhiteSpace(search))
        {
            sql = "SELECT * FROM items WHERE is_favorite = 0 AND is_sensitive = 0 ORDER BY captured_at DESC LIMIT @lim;";
            parameters.Add(("@lim", limit));
        }
        else if (search.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) ||
                 search.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
        {
            var tagPart = search.Contains("::") ? search.Split("::")[1] : search.Split(":")[1];
            tagPart = tagPart.Trim().ToLower();
            int? targetTag = tagPart switch
            {
                "important" or "imp" or "i" or "zhongyao" or "z" => (int)ClipboardItemTag.Important,
                "frequent" or "freq" or "f" or "changyong" or "c" or "cy" => (int)ClipboardItemTag.Frequent,
                "script" or "scr" or "s" or "huashu" or "h" or "hs" => (int)ClipboardItemTag.Script,
                "temporary" or "temp" or "t" or "linshi" or "l" or "ls" => (int)ClipboardItemTag.Temporary,
                _ => null
            };

            if (targetTag.HasValue)
            {
                sql = "SELECT * FROM items WHERE is_favorite = 0 AND is_sensitive = 0 AND tag = @tag ORDER BY captured_at DESC LIMIT @lim;";
                parameters.Add(("@tag", targetTag.Value));
            }
            else
            {
                return new List<ClipboardItemEntity>();
            }
            parameters.Add(("@lim", limit));
        }
        else
        {
            var pattern = $"%{search}%";
            sql = @"SELECT * FROM items
                    WHERE is_favorite = 0 AND is_sensitive = 0
                      AND (text_content LIKE @p OR file_paths LIKE @p OR alias LIKE @p OR ocr_text LIKE @p)
                    ORDER BY captured_at DESC LIMIT @lim;";
            parameters.Add(("@lim", limit));
            parameters.Add(("@p", pattern));
        }

        return await _conn.QueryItemsAsync(sql, parameters.ToArray());
    }

    public async Task<List<ClipboardItemEntity>> GetFavoritesAsync(string? search = null)
    {
        string sql;
        var parameters = new List<(string, object?)>();

        if (string.IsNullOrWhiteSpace(search))
        {
            sql = "SELECT * FROM items WHERE is_favorite = 1 ORDER BY captured_at DESC;";
        }
        else
        {
            var pattern = $"%{search}%";
            sql = @"SELECT * FROM items
                    WHERE is_favorite = 1
                      AND (text_content LIKE @p OR file_paths LIKE @p OR alias LIKE @p)
                    ORDER BY captured_at DESC;";
            parameters.Add(("@p", pattern));
        }

        return await _conn.QueryItemsAsync(sql, parameters.ToArray());
    }

    public async Task<ClipboardItemEntity?> GetByIdAsync(long id)
    {
        var list = await _conn.QueryItemsAsync("SELECT * FROM items WHERE id = @id;", ("@id", id));
        return list.Count > 0 ? list[0] : null;
    }

    public async Task ToggleFavoriteAsync(long id)
    {
        await _conn.ExecAsync(
            "UPDATE items SET is_favorite = CASE WHEN is_favorite = 0 THEN 1 ELSE 0 END WHERE id = @id;",
            ("@id", id));
    }

    public async Task DeleteAsync(long id)
    {
        var item = await GetByIdAsync(id);
        if (item?.ImageBlobPath is { Length: > 0 })
        {
            _blobs.DeleteBlob(item.ImageBlobPath);
        }
        await _conn.ExecAsync("DELETE FROM items WHERE id = @id;", ("@id", id));
    }

    public async Task ClearHistoryAsync()
    {
        var blobs = new List<string>();
        await _conn.Lock.WaitAsync();
        try
        {
            using var cmd = _conn.CreateLockedCommand();
            cmd.CommandText = "SELECT image_blob FROM items WHERE is_favorite = 0 AND is_sensitive = 0 AND image_blob IS NOT NULL;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) blobs.Add(reader.GetString(0));
        }
        finally { _conn.Lock.Release(); }

        foreach (var b in blobs) _blobs.DeleteBlob(b);
        await _conn.ExecAsync("DELETE FROM items WHERE is_favorite = 0 AND is_sensitive = 0;");
    }

    public async Task<int> PurgeExpiredAsync(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(maxAge).ToString("O");
        var blobs = new List<string>();
        await _conn.Lock.WaitAsync();
        try
        {
            using var cmd = _conn.CreateLockedCommand();
            cmd.CommandText = "SELECT image_blob FROM items WHERE is_favorite = 0 AND is_sensitive = 0 AND captured_at < @cut AND image_blob IS NOT NULL;";
            cmd.Parameters.AddWithValue("@cut", cutoff);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) blobs.Add(reader.GetString(0));
        }
        finally { _conn.Lock.Release(); }

        foreach (var b in blobs) _blobs.DeleteBlob(b);

        await _conn.Lock.WaitAsync();
        try
        {
            using var cmd = _conn.CreateLockedCommand();
            cmd.CommandText = "DELETE FROM items WHERE is_favorite = 0 AND is_sensitive = 0 AND captured_at < @cut;";
            cmd.Parameters.AddWithValue("@cut", cutoff);
            return await cmd.ExecuteNonQueryAsync();
        }
        finally { _conn.Lock.Release(); }
    }
}
