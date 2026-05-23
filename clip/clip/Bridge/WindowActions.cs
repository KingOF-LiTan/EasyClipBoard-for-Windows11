using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using clip.Core.Models;
using clip.Core.Storage;

namespace clip.Bridge;

internal sealed class WindowActions
{
    private readonly StorageService _storage;

    public WindowActions(StorageService storage)
    {
        _storage = storage;
    }

    public object HideWindowImmediate()
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
        {
            MainWindow.Current?.HideWindowImmediate();
        });
        return new { success = true };
    }

    public object LogFromJs(JsonElement root)
    {
        string level = root.TryGetProperty("level", out var l) ? l.GetString() ?? "info" : "info";
        string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        System.Diagnostics.Debug.WriteLine($"[WebView2 JS {level.ToUpper()}] {msg}");
        return null!;
    }

    public async Task<object> StartDragAsync(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idElem)) return BridgeResponse.Fail("missingId");
        long itemId = idElem.GetInt64();

        var entity = await _storage.GetItemByIdAsync(itemId);
        if (entity == null) return BridgeResponse.Fail("itemNotFound");

        var filePaths = new List<string>();
        string? textContent = null;

        if (entity.Type == ClipboardItemType.Text)
        {
            textContent = entity.TextContent;
        }
        else if (entity.Type == ClipboardItemType.Image && !string.IsNullOrEmpty(entity.ImageBlobPath))
        {
            var absPath = Path.Combine(_storage.BlobDir, entity.ImageBlobPath);
            if (!File.Exists(absPath) && File.Exists(absPath + ".png")) absPath += ".png";
            if (File.Exists(absPath)) filePaths.Add(absPath);
        }
        else if (entity.Type == ClipboardItemType.Files && !string.IsNullOrEmpty(entity.FilePathsJson))
        {
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(entity.FilePathsJson);
                if (paths != null) filePaths.AddRange(paths.Where(File.Exists));
            }
            catch { }
        }

        if (filePaths.Count == 0 && string.IsNullOrEmpty(textContent))
            return BridgeResponse.Fail("emptyDragData");

        var capturedPaths = filePaths.ToArray();
        var capturedText = textContent;

        MainWindow.Current?.DispatcherQueue.TryEnqueue(() =>
        {
            Native.Win32Helper.ReleaseCapture();
            Native.Win32Helper.mouse_event(Native.Win32Helper.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Native.Win32Helper.mouse_event(Native.Win32Helper.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);

            Native.DragDropService.StartDrag(capturedPaths, capturedText);
        });

        return new { success = true };
    }

    public async Task<object> SelectBackgroundImageAsync()
    {
        var tcs = new TaskCompletionSource<object>();

        if (MainWindow.Current?.DispatcherQueue == null)
            return BridgeResponse.Fail("windowUnavailable");

        MainWindow.Current.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".webp");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    var copiedFile = await file.CopyAsync(localFolder, file.Name, Windows.Storage.NameCollisionOption.GenerateUniqueName);
                    Core.SettingsManager.Set("ui_background_path", copiedFile.Path);

                    byte[] bytes = File.ReadAllBytes(copiedFile.Path);
                    string ext = copiedFile.FileType.TrimStart('.').ToLower();
                    if (ext == "jpg") ext = "jpeg";
                    string base64 = $"data:image/{ext};base64,{Convert.ToBase64String(bytes)}";

                    tcs.TrySetResult(new { success = true, base64 });
                }
                else
                {
                    tcs.TrySetResult(BridgeResponse.Fail("cancelled"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebBridge] Picker error: {ex.Message}");
                tcs.TrySetResult(BridgeResponse.Fail(ex.Message));
            }
        });

        return await tcs.Task;
    }
}
