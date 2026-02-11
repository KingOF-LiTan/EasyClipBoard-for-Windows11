using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using clip.Core.Clipboard;
using clip.Core.Models;
using clip.Core.Storage;
using Microsoft.UI.Dispatching;

namespace clip.ViewModels;

public sealed class MainViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public bool IsEmpty => Items.Count == 0;
    public Microsoft.UI.Xaml.Visibility IsEmptyVisibility => IsEmpty ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public bool HasSelection => Items.Any(i => i.IsSelected);
    public Microsoft.UI.Xaml.Visibility HasSelectionVisibility => HasSelection ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private readonly StorageService _storage;
    private readonly DispatcherQueue _uiDispatcher;

    public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();
    public ObservableCollection<ClipboardItemViewModel> SensitiveItems { get; } = new();

    public bool ShowFavorites { get; set; }
    public Action? OnBeforeClipboardWrite { get; set; }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                _ = RefreshAsync();
            }
        }
    }

    public MainViewModel(StorageService storage, DispatcherQueue uiDispatcher)
    {
        _storage = storage;
        _uiDispatcher = uiDispatcher;
        Items.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsEmptyVisibility));
        };
    }

    /// <summary>
    /// 刷新当前列表。支持分类视图。
    /// </summary>
    public async Task RefreshAsync(string category = "All")
    {
        try
        {
            List<ClipboardItemEntity> entities;

            if (ShowFavorites)
            {
                // 自动化归类逻辑
                var allFavorites = await _storage.GetFavoritesAsync(_searchText);
                entities = category switch
                {
                    "Important" => allFavorites.Where(e => e.Tag == ClipboardItemTag.Important).ToList(),
                    "Frequent" => allFavorites.Where(e => e.Tag == ClipboardItemTag.Frequent).ToList(),
                    "Sensitive" => allFavorites.Where(e => e.IsSensitive).ToList(),
                    _ => allFavorites
                };
            }
            else
            {
                entities = await _storage.GetHistoryAsync(200, _searchText);
            }

            _uiDispatcher.TryEnqueue(() =>
            {
                Items.Clear();
                foreach (var e in entities)
                {
                    Items.Add(new ClipboardItemViewModel(e, _storage));
                }
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] UI 集合已刷新, 数量: {Items.Count}, 分类: {category}");
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 刷新失败: {ex.Message}");
        }
    }

    public async Task PasteItemAsync(ClipboardItemViewModel vm)
    {
        var entity = await _storage.GetItemByIdAsync(vm.Id);
        if (entity == null) return;

        OnBeforeClipboardWrite?.Invoke();
        await ClipboardWriter.WriteAsync(entity, _storage);
    }

    public async Task ToggleFavoriteAsync(ClipboardItemViewModel vm)
    {
        await _storage.ToggleFavoriteAsync(vm.Id);
        await RefreshAsync();
    }

    public async Task ClearAllHistoryAsync()
    {
        await _storage.ClearHistoryAsync();
        await RefreshAsync();
    }

    public async Task UpdateTagAsync(ClipboardItemViewModel vm, ClipboardItemTag tag)
    {
        // Toggle 逻辑：如果已经是该标签则重置为临时
        var newTag = vm.Tag == tag ? ClipboardItemTag.Temporary : tag;

        // 自动收藏逻辑：如果标记为重要，且未收藏，则自动加入收藏
        if (newTag == ClipboardItemTag.Important && !vm.IsFavorite)
        {
            await ToggleFavoriteAsync(vm);
        }

        await _storage.UpdateItemTagAsync(vm.Id, newTag);
        await RefreshAsync();
    }

    public async Task DeleteItemAsync(ClipboardItemViewModel vm)
    {
        await _storage.DeleteItemAsync(vm.Id);
        await RefreshAsync();
    }

    // ── 批量操作 ──

    public void UpdateSelectionState()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSelectionVisibility));
    }

    public void ClearSelection()
    {
        foreach (var item in Items) item.IsSelected = false;
        UpdateSelectionState();
    }

    public async Task DeleteSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        foreach (var item in selected)
        {
            await _storage.DeleteItemAsync(item.Id);
        }
        await RefreshAsync();
        ClearSelection();
    }

    public async Task FavoriteSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        foreach (var item in selected)
        {
            // 如果已收藏，则不做处理；如果未收藏，则收藏
            if (!item.IsFavorite)
            {
                await _storage.ToggleFavoriteAsync(item.Id);
            }
        }
        await RefreshAsync();
        ClearSelection();
    }

    // ── 敏感信息管理 ──

    public async Task RefreshSensitiveAsync(string? search = null)
    {
        try
        {
            var entities = await _storage.GetSensitiveItemsAsync(search);
            _uiDispatcher.TryEnqueue(() =>
            {
                SensitiveItems.Clear();
                foreach (var e in entities)
                {
                    SensitiveItems.Add(new ClipboardItemViewModel(e, _storage));
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 加载敏感信息失败: {ex.Message}");
        }
    }

    public async Task AddManualSecretAsync(string alias, string content, SensitiveType sensitiveType, string? username = null)
    {
        await _storage.AddManualSecretAsync(alias, content, sensitiveType, username);
        await RefreshSensitiveAsync();
    }

    public async Task<string?> GetDecryptedTextAsync(long id)
    {
        var entity = await _storage.GetItemByIdAsync(id);
        if (entity == null || !entity.IsSensitive) return entity?.TextContent;
        return Core.Security.EncryptionService.Decrypt(entity.TextContent ?? "");
    }

    public async Task DeleteSecretAsync(ClipboardItemViewModel vm)
    {
        await _storage.DeleteItemAsync(vm.Id);
        await RefreshSensitiveAsync();
    }
}
