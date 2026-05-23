using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using clip.Core.Models;
using Microsoft.Data.Sqlite;

namespace clip.Core.Storage;

/// <summary>
/// SQLite connection lifecycle, lock, and low-level query/exec helpers.
/// </summary>
internal sealed class StorageConnection : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;
    internal readonly SemaphoreSlim Lock = new(1, 1);

    public StorageConnection(string dbPath)
    {
        _dbPath = dbPath;
    }

    public async Task OpenAsync()
    {
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        await _conn.OpenAsync();
        await ExecInternalAsync("PRAGMA journal_mode=WAL;");
    }

    public async Task ExecAsync(string sql, params (string name, object? value)[] parameters)
    {
        await Lock.WaitAsync();
        try { await ExecInternalAsync(sql, parameters); }
        finally { Lock.Release(); }
    }

    public async Task ExecInternalAsync(string sql, params (string name, object? value)[] parameters)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<object?> ScalarAsync(string sql, params (string name, object? value)[] parameters)
    {
        await Lock.WaitAsync();
        try
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            var result = await cmd.ExecuteScalarAsync();
            return result == DBNull.Value ? null : result;
        }
        finally { Lock.Release(); }
    }

    public async Task<List<ClipboardItemEntity>> QueryItemsAsync(string sql, params (string name, object? value)[] parameters)
    {
        var results = new List<ClipboardItemEntity>();
        await Lock.WaitAsync();
        try
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) results.Add(MapEntity(reader));
        }
        finally { Lock.Release(); }
        return results;
    }

    /// <summary>Run a scalar WITHIN an existing lock held by the caller.</summary>
    public async Task<object?> ScalarWithLockAsync(string sql, params (string name, object? value)[] parameters)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result;
    }

    /// <summary>Run ExecuteNonQuery WITHIN an existing lock held by the caller.</summary>
    public async Task ExecWithLockAsync(string sql, params (string name, object? value)[] parameters)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Create and execute a command WITHIN an existing lock — returns the command for reader access.</summary>
    public SqliteCommand CreateLockedCommand()
    {
        return _conn!.CreateCommand();
    }

    public static ClipboardItemEntity MapEntity(SqliteDataReader r)
    {
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
        Lock.Dispose();
    }
}
