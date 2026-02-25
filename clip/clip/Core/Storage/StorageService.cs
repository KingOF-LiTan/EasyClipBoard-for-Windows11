using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using clip.Core.Models;
using Microsoft.Data.Sqlite;

namespace clip.Core.Storage;

public sealed class StorageService : IDisposable
{
    private readonly string _dbPath;
    private readonly string _blobDir;
    private SqliteConnection? _conn;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public StorageService()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinClipboard");

        Directory.CreateDirectory(baseDir);
        _dbPath = Path.Combine(baseDir, "data.db");
        _blobDir = Path.Combine(baseDir, "blobs");
        Directory.CreateDirectory(_blobDir);
    }

    public string DbPath => _dbPath;

    public async Task InitAsync()
    {
        try
        {
            // 检查是否有恢复文件
            var restorePath = _dbPath + ".restore";
            if (File.Exists(restorePath))
            {
                try
                {
                    File.Move(restorePath, _dbPath, true);
                    System.Diagnostics.Debug.WriteLine("[Storage] 数据库已从备份恢复");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Storage] 恢复失败: {ex.Message}");
                }
            }

            _conn = new SqliteConnection($"Data Source={_dbPath}");
            await _conn.OpenAsync();
            await ExecAsync("PRAGMA journal_mode=WAL;");
            await ExecAsync(@"
                CREATE TABLE IF NOT EXISTS items (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    type            INTEGER NOT NULL,
                    captured_at     TEXT    NOT NULL,
                    is_favorite     INTEGER NOT NULL DEFAULT 0,
                    tag             INTEGER NOT NULL DEFAULT 0,
                    is_sensitive    INTEGER NOT NULL DEFAULT 0,
                    sensitive_type  INTEGER NOT NULL DEFAULT 0,
                    text_content    TEXT,
                    username        TEXT,
                    image_blob      TEXT,
                    image_hash      TEXT,
                    image_w         INTEGER,
                    image_h         INTEGER,
                    file_paths      TEXT
                );");

            // 确保旧表也有相关列
            try { await ExecAsync("ALTER TABLE items ADD COLUMN tag INTEGER NOT NULL DEFAULT 0;"); } catch { }
            try { await ExecAsync("ALTER TABLE items ADD COLUMN is_sensitive INTEGER NOT NULL DEFAULT 0;"); } catch { }
            try { await ExecAsync("ALTER TABLE items ADD COLUMN sensitive_type INTEGER NOT NULL DEFAULT 0;"); } catch { }
            try { await ExecAsync("ALTER TABLE items ADD COLUMN username TEXT;"); } catch { }
            try { await ExecAsync("ALTER TABLE items ADD COLUMN alias TEXT;"); } catch { }
            try { await ExecAsync("ALTER TABLE items ADD COLUMN remark TEXT;"); } catch { }

            await ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_captured ON items(captured_at);");
            await ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_favorite ON items(is_favorite);");
            await ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_hash ON items(image_hash);");
            await ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_tag ON items(tag);");
            await ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_sensitive ON items(is_sensitive);");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Storage] 初始化失败: {ex.Message}");
            throw;
        }
    }

    public async Task<long> SaveItemAsync(ClipboardItemDraft draft)
    {
        await _lock.WaitAsync();
        try
        {
            string? hash = null;
            string? blobRelPath = null;

            if (draft.Type == ClipboardItemType.Image && draft.ImagePng is { Length: > 0 })
            {
                hash = ComputeSha256(draft.ImagePng);
                var existingId = await ScalarInternalAsync("SELECT id FROM items WHERE image_hash = @h LIMIT 1;", ("@h", hash));

                if (existingId != null)
                {
                    await ExecInternalAsync("UPDATE items SET captured_at = @t WHERE id = @id;", 
                        ("@t", DateTimeOffset.UtcNow.ToString("O")), ("@id", (long)existingId));
                    return (long)existingId;
                }

                var fileName = $"{Guid.NewGuid():N}.png";
                await File.WriteAllBytesAsync(Path.Combine(_blobDir, fileName), draft.ImagePng);
                blobRelPath = fileName;
            }

            string? filePathsJson = null;
            if (draft.FilePaths is { Count: > 0 })
            {
                filePathsJson = JsonSerializer.Serialize(draft.FilePaths);
            }

            // 处理敏感加密
            string? textToSave = draft.Text;
            bool isSensitive = draft.IsSensitive || (draft.Tag == ClipboardItemTag.Important && draft.Type == ClipboardItemType.Text);
            if (isSensitive && !string.IsNullOrEmpty(textToSave))
            {
                textToSave = Security.EncryptionService.Encrypt(textToSave);
            }

            var sql = @"
                INSERT INTO items (type, captured_at, tag, is_sensitive, sensitive_type, text_content, username, remark, image_blob, image_hash, image_w, image_h, file_paths, alias)
                VALUES (@type, @cap, @tag, @sens, @stype, @txt, @user, @remark, @blob, @hash, @iw, @ih, @fp, @alias);
                SELECT last_insert_rowid();";

            var id = await ScalarInternalAsync(sql,
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
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateItemTagAsync(long id, ClipboardItemTag tag)
    {
        await ExecAsync("UPDATE items SET tag = @tag WHERE id = @id;", ("@tag", (int)tag), ("@id", id));
    }

    public async Task UpdateSensitiveFlagAsync(long id, bool isSensitive)
    {
        if (isSensitive)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null && !item.IsSensitive && !string.IsNullOrEmpty(item.TextContent))
            {
                var encrypted = Security.EncryptionService.Encrypt(item.TextContent);
                await ExecAsync("UPDATE items SET is_sensitive = 1, text_content = @txt WHERE id = @id;", 
                    ("@txt", encrypted), ("@id", id));
                return;
            }
        }
        await ExecAsync("UPDATE items SET is_sensitive = @s WHERE id = @id;", 
            ("@s", isSensitive ? 1 : 0), ("@id", id));
    }

    public async Task UpdateItemAliasAsync(long id, string? alias)
    {
        await ExecAsync("UPDATE items SET alias = @a WHERE id = @id;", ("@a", (object?)alias ?? DBNull.Value), ("@id", id));
    }

    public async Task<List<ClipboardItemEntity>> GetHistoryAsync(int limit = 100, string? search = null)
    {
        string sql;
        var parameters = new List<(string, object?)>();

        if (string.IsNullOrWhiteSpace(search))
        {
            sql = "SELECT * FROM items WHERE is_favorite = 0 ORDER BY captured_at DESC LIMIT @lim;";
            parameters.Add(("@lim", limit));
        }
        else if (search.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) || search.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
        {
            // 支持 tag::z 或 t::z 语法
            var tagPart = search.Contains("::") ? search.Split("::")[1] : search.Split(":")[1];
            tagPart = tagPart.Trim().ToLower();

            // 拼音首字母匹配
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
                sql = "SELECT * FROM items WHERE is_favorite = 0 AND tag = @tag ORDER BY captured_at DESC LIMIT @lim;";
                parameters.Add(("@tag", targetTag.Value));
            }
            else
            {
                // 未知 tag，搜不到结果
                return new List<ClipboardItemEntity>();
            }
            parameters.Add(("@lim", limit));
        }
        else
        {
            var pattern = $"%{search}%";
            // 增加 alias 搜索
            sql = @"SELECT * FROM items 
                    WHERE is_favorite = 0 
                      AND (text_content LIKE @p OR file_paths LIKE @p OR alias LIKE @p)
                    ORDER BY captured_at DESC LIMIT @lim;";
            parameters.Add(("@lim", limit));
            parameters.Add(("@p", pattern));
        }

        return await QueryAsync(sql, parameters.ToArray());
    }

    public async Task<List<ClipboardItemEntity>> GetFavoritesAsync(string? search = null)
    {
        // 自动化归类查询：目前返回所有收藏项，后续可在 VM 层分文件夹展示
        string sql;
        var parameters = new List<(string, object?)>();

        if (string.IsNullOrWhiteSpace(search))
        {
            sql = "SELECT * FROM items WHERE is_favorite = 1 ORDER BY captured_at DESC;";
        }
        else
        {
            var pattern = $"%{search}%";
            // 增加 alias 搜索
            sql = @"SELECT * FROM items 
                    WHERE is_favorite = 1 
                      AND (text_content LIKE @p OR file_paths LIKE @p OR alias LIKE @p)
                    ORDER BY captured_at DESC;";
            parameters.Add(("@p", pattern));
        }

        return await QueryAsync(sql, parameters.ToArray());
    }

    /// <summary>
    /// 获取所有敏感信息条目
    /// </summary>
    public async Task<List<ClipboardItemEntity>> GetSensitiveItemsAsync(string? search = null)
    {
        string sql;
        var parameters = new List<(string, object?)>();

        if (string.IsNullOrWhiteSpace(search))
        {
            sql = "SELECT * FROM items WHERE is_sensitive = 1 ORDER BY captured_at DESC;";
        }
        else
        {
            var pattern = $"%{search}%";
            sql = @"SELECT * FROM items 
                    WHERE is_sensitive = 1 
                      AND (alias LIKE @p OR text_content LIKE @p OR username LIKE @p)
                    ORDER BY captured_at DESC;";
            parameters.Add(("@p", pattern));
        }

        return await QueryAsync(sql, parameters.ToArray());
    }

    /// <summary>
    /// 手动添加敏感条目
    /// </summary>
    public async Task<long> AddManualSecretAsync(string alias, string content, SensitiveType sensitiveType, string? username = null, string? remark = null)
    {
        var encrypted = Security.EncryptionService.Encrypt(content);
        var sql = @"
            INSERT INTO items (type, captured_at, tag, is_sensitive, sensitive_type, text_content, username, remark, alias)
            VALUES (@type, @cap, @tag, 1, @stype, @txt, @user, @rem, @alias);
            SELECT last_insert_rowid();";

        await _lock.WaitAsync();
        try
        {
            var id = await ScalarInternalAsync(sql,
                ("@type", (int)ClipboardItemType.Text),
                ("@cap", DateTimeOffset.UtcNow.ToString("O")),
                ("@tag", (int)ClipboardItemTag.Important),
                ("@stype", (int)sensitiveType),
                ("@txt", encrypted),
                ("@user", (object?)username ?? DBNull.Value),
                ("@rem", (object?)remark ?? DBNull.Value),
                ("@alias", (object?)alias ?? DBNull.Value));
            return (long)(id ?? -1);
        }
        finally { _lock.Release(); }
    }

    public async Task<ClipboardItemEntity?> GetItemByIdAsync(long id)
    {
        var list = await QueryAsync("SELECT * FROM items WHERE id = @id;", ("@id", id));
        return list.Count > 0 ? list[0] : null;
    }

    public string? GetBlobFullPath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        var full = Path.Combine(_blobDir, relativePath);
        return File.Exists(full) ? full : null;
    }

    public async Task ToggleFavoriteAsync(long id)
    {
        await ExecAsync("UPDATE items SET is_favorite = CASE WHEN is_favorite = 0 THEN 1 ELSE 0 END WHERE id = @id;", ("@id", id));
    }

    public async Task DeleteItemAsync(long id)
    {
        var item = await GetItemByIdAsync(id);
        if (item?.ImageBlobPath is { Length: > 0 })
        {
            try { File.Delete(Path.Combine(_blobDir, item.ImageBlobPath)); } catch { }
        }
        await ExecAsync("DELETE FROM items WHERE id = @id;", ("@id", id));
    }

    public async Task ClearHistoryAsync()
    {
        var blobs = new List<string>();
        await _lock.WaitAsync();
        try
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = "SELECT image_blob FROM items WHERE is_favorite = 0 AND image_blob IS NOT NULL;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) blobs.Add(reader.GetString(0));
        }
        finally { _lock.Release(); }

        foreach (var b in blobs) try { File.Delete(Path.Combine(_blobDir, b)); } catch { }
        await ExecAsync("DELETE FROM items WHERE is_favorite = 0;");
    }

    public async Task<int> PurgeExpiredAsync(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(maxAge).ToString("O");
        var blobs = new List<string>();
        await _lock.WaitAsync();
        try
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = "SELECT image_blob FROM items WHERE is_favorite = 0 AND captured_at < @cut AND image_blob IS NOT NULL;";
            cmd.Parameters.AddWithValue("@cut", cutoff);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) blobs.Add(reader.GetString(0));
        }
        finally { _lock.Release(); }

        foreach (var b in blobs) try { File.Delete(Path.Combine(_blobDir, b)); } catch { }
        
        await _lock.WaitAsync();
        try
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = "DELETE FROM items WHERE is_favorite = 0 AND captured_at < @cut;";
            cmd.Parameters.AddWithValue("@cut", cutoff);
            return await cmd.ExecuteNonQueryAsync();
        }
        finally { _lock.Release(); }
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task ExecAsync(string sql, params (string name, object? value)[] parameters)
    {
        await _lock.WaitAsync();
        try { await ExecInternalAsync(sql, parameters); }
        finally { _lock.Release(); }
    }

    private async Task ExecInternalAsync(string sql, params (string name, object? value)[] parameters)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarInternalAsync(string sql, params (string name, object? value)[] parameters)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result;
    }

    private async Task<List<ClipboardItemEntity>> QueryAsync(string sql, params (string name, object? value)[] parameters)
    {
        var results = new List<ClipboardItemEntity>();
        await _lock.WaitAsync();
        try
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) results.Add(MapEntity(reader));
        }
        finally { _lock.Release(); }
        return results;
    }

    private static ClipboardItemEntity MapEntity(SqliteDataReader r)
    {
        // 安全读取 sensitive_type 列（兼容旧数据库）
        int sensitiveTypeVal = 0;
        try { var ord = r.GetOrdinal("sensitive_type"); if (!r.IsDBNull(ord)) sensitiveTypeVal = r.GetInt32(ord); } catch { }

        return new ClipboardItemEntity
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Type = (ClipboardItemType)r.GetInt32(r.GetOrdinal("type")),
            CapturedAtUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("captured_at"))),
            IsFavorite = r.GetInt32(r.GetOrdinal("is_favorite")) != 0,
            Tag = (ClipboardItemTag)r.GetInt32(r.GetOrdinal("tag")),
            IsSensitive = r.GetInt32(r.GetOrdinal("is_sensitive")) != 0,
            SensitiveType = (SensitiveType)sensitiveTypeVal,
            TextContent = r.IsDBNull(r.GetOrdinal("text_content")) ? null : r.GetString(r.GetOrdinal("text_content")),
            Username = !r.IsDBNull(r.GetOrdinal("username")) ? r.GetString(r.GetOrdinal("username")) : null,
            ImageBlobPath = r.IsDBNull(r.GetOrdinal("image_blob")) ? null : r.GetString(r.GetOrdinal("image_blob")),
            ImageHash = r.IsDBNull(r.GetOrdinal("image_hash")) ? null : r.GetString(r.GetOrdinal("image_hash")),
            ImageWidth = r.IsDBNull(r.GetOrdinal("image_w")) ? null : r.GetInt32(r.GetOrdinal("image_w")),
            ImageHeight = r.IsDBNull(r.GetOrdinal("image_h")) ? null : r.GetInt32(r.GetOrdinal("image_h")),
            FilePathsJson = r.IsDBNull(r.GetOrdinal("file_paths")) ? null : r.GetString(r.GetOrdinal("file_paths")),
            Alias = !r.IsDBNull(r.GetOrdinal("alias")) ? r.GetString(r.GetOrdinal("alias")) : null,
            Remark = !r.IsDBNull(r.GetOrdinal("remark")) ? r.GetString(r.GetOrdinal("remark")) : null,
        };
    }

    public void Dispose()
    {
        _conn?.Dispose();
        _lock.Dispose();
    }
}
