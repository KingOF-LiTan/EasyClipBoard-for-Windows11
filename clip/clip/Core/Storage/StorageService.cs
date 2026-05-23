using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using clip.Core.Models;

namespace clip.Core.Storage;

/// <summary>
/// Facade retaining the existing public API. Internal logic is delegated to
/// <see cref="StorageConnection"/>, <see cref="StorageMigrator"/>, <see cref="BlobStore"/>,
/// <see cref="ClipboardItemRepository"/>, and <see cref="VaultRepository"/>.
/// </summary>
public sealed class StorageService : IDisposable
{
    private readonly string _dbPath;
    private readonly StorageConnection _conn;
    private readonly StorageMigrator _migrator;
    private readonly BlobStore _blobs;
    private readonly ClipboardItemRepository _items;
    private readonly VaultRepository _vault;

    public StorageService()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinClipboard");
        Directory.CreateDirectory(baseDir);

        _dbPath = Path.Combine(baseDir, "data.db");
        var blobDir = Path.Combine(baseDir, "blobs");

        _conn = new StorageConnection(_dbPath);
        _migrator = new StorageMigrator(_conn);
        _blobs = new BlobStore(blobDir);
        _items = new ClipboardItemRepository(_conn, _blobs);
        _vault = new VaultRepository(_conn);
    }

    public string DbPath => _dbPath;
    public string BlobDir => _blobs.BlobDir;

    public async Task InitAsync()
    {
        try
        {
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

            await _conn.OpenAsync();
            await _migrator.InitializeDatabaseAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Storage] 初始化失败: {ex.Message}");
            throw;
        }
    }

    // ── ClipboardItemRepository delegate ──

    public Task<long> SaveItemAsync(ClipboardItemDraft draft) =>
        _items.SaveItemAsync(draft);

    public async Task<List<ClipboardItemEntity>> GetHistoryAsync(int limit = 100, string? search = null) =>
        await _items.GetHistoryAsync(limit, search);

    public async Task<List<ClipboardItemEntity>> GetFavoritesAsync(string? search = null) =>
        await _items.GetFavoritesAsync(search);

    public async Task<ClipboardItemEntity?> GetItemByIdAsync(long id) =>
        await _items.GetByIdAsync(id);

    public Task ToggleFavoriteAsync(long id) =>
        _items.ToggleFavoriteAsync(id);

    public Task UpdateItemTagAsync(long id, ClipboardItemTag tag) =>
        _items.UpdateTagAsync(id, tag);

    public Task UpdateSensitiveFlagAsync(long id, bool isSensitive) =>
        _items.UpdateSensitiveFlagAsync(id, isSensitive);

    public Task UpdateOcrTextAsync(long id, string ocrText) =>
        _items.UpdateOcrTextAsync(id, ocrText);

    public Task DeleteItemAsync(long id) =>
        _items.DeleteAsync(id);

    public Task ClearHistoryAsync() =>
        _items.ClearHistoryAsync();

    public Task<int> PurgeExpiredAsync(TimeSpan maxAge) =>
        _items.PurgeExpiredAsync(maxAge);

    // ── VaultRepository delegate ──

    public async Task<List<ClipboardItemEntity>> GetSensitiveItemsAsync(string? search = null) =>
        await _vault.GetSensitiveItemsAsync(search);

    public Task<long> AddManualSecretAsync(
        string alias, string content, SensitiveType sensitiveType,
        string? username = null, string? remark = null) =>
        _vault.AddManualSecretAsync(alias, content, sensitiveType, username, remark);

    public Task UpdateItemAliasAsync(long id, string? alias) =>
        _vault.UpdateAliasAsync(id, alias);

    // ── BlobStore delegate ──

    public string? GetBlobFullPath(string? relativePath) =>
        _blobs.GetFullPath(relativePath);

    // ── Cleanup ──

    public void Dispose()
    {
        _conn.Dispose();
    }
}
