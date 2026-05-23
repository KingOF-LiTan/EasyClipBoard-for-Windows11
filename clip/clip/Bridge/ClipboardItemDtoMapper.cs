using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using clip.Core.Models;
using clip.Core.Storage;

namespace clip.Bridge;

internal sealed class ClipboardItemDtoMapper
{
    private readonly StorageService _storage;

    public ClipboardItemDtoMapper(StorageService storage)
    {
        _storage = storage;
    }

    public object Map(ClipboardItemEntity e)
    {
        string preview = "";
        if (e.IsSensitive)
        {
            preview = "********";
        }
        else if (e.Type == ClipboardItemType.Text && !string.IsNullOrEmpty(e.TextContent))
        {
            var lines = e.TextContent.Split('\n');
            preview = string.Join("\n", lines.Length > 4 ? lines[..4] : lines).TrimEnd('\r', '\n');
            if (lines.Length > 4) preview += " ...";
        }
        else if (e.Type == ClipboardItemType.Files && !string.IsNullOrEmpty(e.FilePathsJson))
        {
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(e.FilePathsJson);
                if (paths is { Count: > 0 })
                    preview = string.Join(", ", paths.Select(Path.GetFileName));
            }
            catch { preview = "[文件]"; }
        }
        else if (e.Type == ClipboardItemType.Image)
        {
            preview = $"[图片] {(e.ImageWidth.HasValue ? $"{e.ImageWidth}x{e.ImageHeight}" : "")}";
        }

        string? colorHex = null;
        if (e.Type == ClipboardItemType.Text && !string.IsNullOrEmpty(e.TextContent))
        {
            var trimmed = e.TextContent.Trim();
            if (Regex.IsMatch(trimmed, @"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$"))
                colorHex = trimmed;
        }

        bool hasImage = e.Type == ClipboardItemType.Image
            && !string.IsNullOrEmpty(e.ImageBlobPath)
            && !string.IsNullOrEmpty(_storage.GetBlobFullPath(e.ImageBlobPath));

        return new
        {
            id = e.Id,
            type = e.Type.ToString().ToLower(),
            tag = e.Tag.ToString(),
            preview,
            colorHex,
            alias = e.Alias,
            isFavorite = e.IsFavorite,
            isSensitive = e.IsSensitive,
            sensitiveType = e.SensitiveType.ToString(),
            username = e.Username,
            remark = e.Remark,
            capturedAt = e.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            timeAgo = FormatTimeAgo(e.CapturedAtUtc),
            hasImage,
            imageWidth = e.ImageWidth,
            imageHeight = e.ImageHeight,
        };
    }

    private static string FormatTimeAgo(DateTimeOffset utc)
    {
        var diff = DateTimeOffset.Now - utc;
        if (diff.TotalSeconds < 60) return "刚刚";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}小时前";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}天前";
        return utc.ToLocalTime().ToString("MM/dd");
    }
}
