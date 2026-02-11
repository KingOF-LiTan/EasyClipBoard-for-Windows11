using System;
using clip.Core.Models;

namespace clip.Core.Storage;

/// <summary>
/// 数据库实体，映射 items 表中的一行记录。
/// </summary>
public sealed class ClipboardItemEntity
{
    public long Id { get; set; }

    /// <summary>1=Text, 2=Image, 3=Files</summary>
    public ClipboardItemType Type { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    public bool IsFavorite { get; set; }

    // ── 文本 ──
    public string? TextContent { get; set; }

    // ── 图片 ──
    /// <summary>Blob 文件相对路径（相对于 blobs 目录）</summary>
    public string? ImageBlobPath { get; set; }

    /// <summary>SHA256 hex，用于去重</summary>
    public string? ImageHash { get; set; }

    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }

    // ── 文件列表 ──
    /// <summary>JSON 数组序列化的文件路径列表</summary>
    public string? FilePathsJson { get; set; }

    /// <summary>标签：0=临时, 1=重要, 2=常用, 3=话术</summary>
    public ClipboardItemTag Tag { get; set; }

    /// <summary>是否为敏感内容（加密存储）</summary>
    public bool IsSensitive { get; set; }

    /// <summary>敏感内容类型</summary>
    public SensitiveType SensitiveType { get; set; }

    /// <summary>用户名/账号（仅SensitiveType=Credential等需要时使用）</summary>
    public string? Username { get; set; }

    /// <summary>用户自定义的别名/备注名</summary>
    public string? Alias { get; set; }
}
