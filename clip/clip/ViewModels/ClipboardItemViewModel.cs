using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using clip.Core.Models;
using clip.Core.Storage;
using clip.Core.Security;

namespace clip.ViewModels;

public sealed class ClipboardItemViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly ClipboardItemEntity _entity;
    private readonly StorageService _storage;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public ClipboardItemViewModel(ClipboardItemEntity entity, StorageService storage)
    {
        _entity = entity;
        _storage = storage;
    }

    public long Id => _entity.Id;
    public ClipboardItemType Type => _entity.Type;
    public ClipboardItemTag Tag => _entity.Tag;
    public SensitiveType SensitiveType => _entity.SensitiveType;
    public string? Username => _entity.Username;
    public bool IsSensitive => _entity.IsSensitive;
    public DateTimeOffset CapturedAtUtc => _entity.CapturedAtUtc;

    public Microsoft.UI.Xaml.Visibility UsernameVisibility => !string.IsNullOrEmpty(Username) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    // ─── 标签交互属性 ───

    public bool IsFavorite
    {
        get => _entity.IsFavorite;
        set
        {
            if (_entity.IsFavorite != value)
            {
                _entity.IsFavorite = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>用户自定义的别名 (支持搜索)</summary>
    public string? Alias
    {
        get => _entity.Alias;
        set
        {
            if (_entity.Alias != value)
            {
                _entity.Alias = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName)); // 确保显示名称同步刷新
                // 自动保存别名更改
                _ = _storage.UpdateItemAliasAsync(Id, value);
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSelectedVisibility));
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility IsSelectedVisibility => IsSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public double ImportantOpacity => Tag == ClipboardItemTag.Important ? 1.0 : 0.7;
    public double FrequentOpacity => Tag == ClipboardItemTag.Frequent ? 1.0 : 0.7;
    public double ScriptOpacity => Tag == ClipboardItemTag.Script ? 1.0 : 0.7;
    public double TemporaryOpacity => Tag == ClipboardItemTag.Temporary ? 1.0 : 0.7;

    public Brush ImportantBrush => Tag == ClipboardItemTag.Important ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    public Brush FrequentBrush => Tag == ClipboardItemTag.Frequent ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"] : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    public Brush ScriptBrush => Tag == ClipboardItemTag.Script ? (Brush)Application.Current.Resources["SystemFillColorAttentionBrush"] : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    public Brush TemporaryBrush => Tag == ClipboardItemTag.Temporary ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    // ─── 右下角属性标识 ───

    public string TagName => Tag switch
    {
        ClipboardItemTag.Important => "重要",
        ClipboardItemTag.Frequent => "常用",
        ClipboardItemTag.Script => "话术",
        _ => "临时"
    };

    public Brush TagBackground => Tag switch
    {
        ClipboardItemTag.Important => new SolidColorBrush(Windows.UI.Color.FromArgb(80, 255, 0, 0)),
        ClipboardItemTag.Frequent => new SolidColorBrush(Windows.UI.Color.FromArgb(80, 0, 255, 0)),
        ClipboardItemTag.Script => new SolidColorBrush(Windows.UI.Color.FromArgb(80, 0, 120, 215)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(60, 128, 128, 128))
    };

    public Brush TagForeground => Tag switch
    {
        ClipboardItemTag.Important => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        ClipboardItemTag.Frequent => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        ClipboardItemTag.Script => (Brush)Application.Current.Resources["SystemFillColorAttentionBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    private bool _isDecrypted = false;
    public bool IsDecrypted
    {
        get => _isDecrypted;
        set
        {
            if (_isDecrypted != value)
            {
                _isDecrypted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Preview));
                OnPropertyChanged(nameof(MaskedPreview));
            }
        }
    }

    // ─── 内容预览 (解密支持) ───

    public string Preview
    {
        get
        {
            if (_entity.IsSensitive && !IsDecrypted)
            {
                return "******** (点击图标解密查看)";
            }

            string content = _entity.TextContent ?? "";
            if (_entity.IsSensitive)
            {
                content = EncryptionService.Decrypt(content);
            }

            if (_entity.Type == ClipboardItemType.Text && !string.IsNullOrEmpty(content))
            {
                var lines = content.Split('\n');
                var preview = string.Join("\n", lines.Length > 3 ? lines[..3] : lines);
                if (lines.Length > 3) preview += " …";
                return preview.TrimEnd('\r', '\n');
            }

            if (_entity.Type == ClipboardItemType.Files && !string.IsNullOrEmpty(_entity.FilePathsJson))
            {
                try
                {
                    var paths = JsonSerializer.Deserialize<List<string>>(_entity.FilePathsJson);
                    if (paths is { Count: > 0 })
                    {
                        var names = new List<string>();
                        foreach (var p in paths) names.Add(Path.GetFileName(p) ?? p);
                        return string.Join(", ", names);
                    }
                }
                catch { return "[文件读取错误]"; }
            }

            if (_entity.Type == ClipboardItemType.Image)
            {
                return $"[图片] {(_entity.ImageWidth.HasValue ? $"{_entity.ImageWidth}×{_entity.ImageHeight}" : "")}";
            }

            return "[空]";
        }
    }

    /// <summary>显示名称：优先显示别名，其次账号，无别名显示"未命名"</summary>
    public string DisplayName => !string.IsNullOrEmpty(Alias) ? Alias : (!string.IsNullOrEmpty(Username) ? Username : "未命名");

    // ── 敏感信息展示 ──

    /// <summary>敏感类型图标</summary>
    public string SensitiveTypeIcon => SensitiveType switch
    {
        SensitiveType.Password => "\uE8D7",       // Lock
        SensitiveType.Credential => "\uE77B",      // Contact
        SensitiveType.ApiKey => "\uE8D7",           // Key
        SensitiveType.PrivateKey => "\uE8A7",       // Certificate
        SensitiveType.ConnectionString => "\uE968", // Database
        _ => "\uE72E"                               // Shield
    };

    /// <summary>敏感类型名称</summary>
    public string SensitiveTypeName => SensitiveType switch
    {
        SensitiveType.Password => "密码",
        SensitiveType.Credential => "账号密码",
        SensitiveType.ApiKey => "API Key",
        SensitiveType.PrivateKey => "私钥",
        SensitiveType.ConnectionString => "连接串",
        _ => "敏感信息"
    };

    /// <summary>掩码预览：显示前4+后3字符，中间用****</summary>
    public string MaskedPreview
    {
        get
        {
            if (!IsSensitive) return Preview;
            if (IsDecrypted) return GetDecryptedText();

            var raw = _entity.TextContent ?? "";
            // 加密的内容无法直接掩码，显示固定掩码
            return "****····****";
        }
    }

    /// <summary>解密后的明文</summary>
    public string GetDecryptedText()
    {
        if (!IsSensitive || string.IsNullOrEmpty(_entity.TextContent))
            return _entity.TextContent ?? "";
        return EncryptionService.Decrypt(_entity.TextContent);
    }

    public string TypeIcon => _entity.Type switch
    {
        ClipboardItemType.Text => "\uE8C1",
        ClipboardItemType.Image => "\uE8B9",
        ClipboardItemType.Files => "\uE8B7",
        _ => "\uE8C1"
    };

    private BitmapImage? _thumbnail;
    public BitmapImage? Thumbnail
    {
        get
        {
            if (_entity.Type != ClipboardItemType.Image) return null;
            if (_thumbnail != null) return _thumbnail;

            var fullPath = _storage.GetBlobFullPath(_entity.ImageBlobPath);
            if (fullPath != null && File.Exists(fullPath))
            {
                _thumbnail = new BitmapImage(new Uri(fullPath)) { DecodePixelWidth = 400 };
            }
            return _thumbnail;
        }
    }

    public Visibility ThumbnailVisibility => _entity.Type == ClipboardItemType.Image ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TextVisibility => _entity.Type == ClipboardItemType.Image ? Visibility.Collapsed : Visibility.Visible;

    public string TimeAgo
    {
        get
        {
            var diff = DateTimeOffset.UtcNow - _entity.CapturedAtUtc;
            if (diff.TotalSeconds < 60) return "刚刚";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} 分钟前";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时前";
            return _entity.CapturedAtUtc.ToLocalTime().ToString("M月d日");
        }
    }

    public void UpdateTag(ClipboardItemTag newTag)
    {
        // 实际上这应该由 ViewModel 调用 StorageService 并通知 MainViewModel 刷新
    }
}
