using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using clip.Core.Storage;
using clip.Core.Models;

namespace clip.Core.Clipboard;

/// <summary>
/// 将 ClipboardItemEntity 的内容写回系统剪贴板，供用户粘贴。
/// </summary>
public static class ClipboardWriter
{
    /// <summary>
    /// 写回前需要外部设置抑制标志防止 "自写自读"。
    /// </summary>
    public static async Task WriteAsync(ClipboardItemEntity item, StorageService storage)
    {
        var dp = new DataPackage();

        switch (item.Type)
        {
            case ClipboardItemType.Text:
                if (!string.IsNullOrEmpty(item.TextContent))
                {
                    dp.SetText(item.TextContent);
                }
                break;

            case ClipboardItemType.Image:
                if (!string.IsNullOrEmpty(item.ImageBlobPath))
                {
                    var fullPath = storage.GetBlobFullPath(item.ImageBlobPath);
                    if (fullPath != null)
                    {
                        var file = await StorageFile.GetFileFromPathAsync(fullPath);
                        dp.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
                    }
                }
                break;

            case ClipboardItemType.Files:
                if (!string.IsNullOrEmpty(item.FilePathsJson))
                {
                    var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(item.FilePathsJson);
                    if (paths is { Count: > 0 })
                    {
                        var storageItems = new List<IStorageItem>();
                        foreach (var p in paths)
                        {
                            try
                            {
                                if (Directory.Exists(p))
                                    storageItems.Add(await StorageFolder.GetFolderFromPathAsync(p));
                                else if (File.Exists(p))
                                    storageItems.Add(await StorageFile.GetFileFromPathAsync(p));
                            }
                            catch { /* 源文件可能已被删除 */ }
                        }

                        if (storageItems.Count > 0)
                            dp.SetStorageItems(storageItems, readOnly: false);
                    }
                }
                break;
        }

        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }
}
