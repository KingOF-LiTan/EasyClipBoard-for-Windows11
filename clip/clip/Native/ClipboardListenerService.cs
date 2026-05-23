using System;
using Microsoft.UI.Dispatching;

namespace clip.Native;

/// <summary>
/// Adds/removes WM_CLIPBOARDUPDATE listener and dispatches change events.
/// </summary>
internal sealed class ClipboardListenerService
{
    private readonly DispatcherQueue _uiQueue;

    public event Action? ClipboardChanged;

    public ClipboardListenerService(DispatcherQueue uiQueue)
    {
        _uiQueue = uiQueue;
    }

    public void Attach(IntPtr hwnd)
    {
        Win32Helper.AddClipboardFormatListener(hwnd);
    }

    public void Detach(IntPtr hwnd)
    {
        try
        {
            if (hwnd != IntPtr.Zero)
                Win32Helper.RemoveClipboardFormatListener(hwnd);
        }
        catch { }
    }

    public bool HandleMessage(uint msg)
    {
        if (msg == Win32Helper.WM_CLIPBOARDUPDATE)
        {
            _uiQueue.TryEnqueue(() => ClipboardChanged?.Invoke());
            return true;
        }
        return false;
    }
}
