using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using clip.Core.Models;

namespace clip.Core.Storage;

/// <summary>
/// Sensitive vault items: list, add, and alias management.
/// </summary>
internal sealed class VaultRepository
{
    private readonly StorageConnection _conn;

    public VaultRepository(StorageConnection conn)
    {
        _conn = conn;
    }

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

        return await _conn.QueryItemsAsync(sql, parameters.ToArray());
    }

    public async Task<long> AddManualSecretAsync(
        string alias, string content, SensitiveType sensitiveType,
        string? username = null, string? remark = null)
    {
        var encrypted = Security.EncryptionService.Encrypt(content);
        var sql = @"
            INSERT INTO items (type, captured_at, tag, is_sensitive, sensitive_type, text_content, username, remark, alias)
            VALUES (@type, @cap, @tag, 1, @stype, @txt, @user, @rem, @alias);
            SELECT last_insert_rowid();";

        await _conn.Lock.WaitAsync();
        try
        {
            var id = await _conn.ScalarWithLockAsync(sql,
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
        finally { _conn.Lock.Release(); }
    }

    public async Task UpdateAliasAsync(long id, string? alias)
    {
        await _conn.ExecAsync("UPDATE items SET alias = @a WHERE id = @id;",
            ("@a", (object?)alias ?? DBNull.Value), ("@id", id));
    }
}
