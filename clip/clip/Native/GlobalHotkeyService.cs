using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace clip.Native;

/// <summary>
/// Registers main toggle + plain-text paste hotkeys, handles WM_HOTKEY dispatch,
/// and supports runtime rebind via WM_APP_REBIND.
/// </summary>
internal sealed class GlobalHotkeyService
{
    public const int HOTKEY_ID_TOGGLE = 1001;
    public const int HOTKEY_ID_PASTE_PLAIN = 1002;
    public const int WM_APP_REBIND = 0x8001;

    private readonly DispatcherQueue _uiQueue;

    public event Action? ToggleRequested;
    public event Action? PlainTextPasteRequested;

    private uint _pendingMod;
    private uint _pendingVk;
    private TaskCompletionSource<bool>? _pendingRebindTcs;

    public GlobalHotkeyService(DispatcherQueue uiQueue)
    {
        _uiQueue = uiQueue;
    }

    public void RegisterInitial(IntPtr hwnd)
    {
        uint modifiers = Core.SettingsManager.Get("hotkey_modifiers", Win32Helper.MOD_CONTROL);
        uint vk = Core.SettingsManager.Get("hotkey_vk", Win32Helper.VK_TAB);
        Win32Helper.RegisterHotKey(hwnd, HOTKEY_ID_TOGGLE, modifiers, vk);

        Win32Helper.RegisterHotKey(hwnd, HOTKEY_ID_PASTE_PLAIN,
            Win32Helper.MOD_CONTROL | Win32Helper.MOD_SHIFT, Win32Helper.VK_V);
    }

    public bool Rebind(IntPtr hwnd, uint modifiers, uint vk)
    {
        if (hwnd == IntPtr.Zero) return false;

        var tcs = new TaskCompletionSource<bool>();
        lock (this)
        {
            _pendingMod = modifiers;
            _pendingVk = vk;
            _pendingRebindTcs = tcs;
        }

        Win32Helper.PostMessage(hwnd, WM_APP_REBIND, IntPtr.Zero, IntPtr.Zero);
        return tcs.Task.GetAwaiter().GetResult();
    }

    public bool HandleMessage(uint msg, IntPtr wParam, IntPtr lParam, IntPtr hwnd)
    {
        if (msg == Win32Helper.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID_TOGGLE)
            {
                _uiQueue.TryEnqueue(() => ToggleRequested?.Invoke());
                return true;
            }
            if (id == HOTKEY_ID_PASTE_PLAIN)
            {
                _uiQueue.TryEnqueue(() => PlainTextPasteRequested?.Invoke());
                return true;
            }
        }

        if (msg == WM_APP_REBIND)
        {
            uint newMod, newVk;
            TaskCompletionSource<bool>? tcs;
            lock (this)
            {
                newMod = _pendingMod;
                newVk = _pendingVk;
                tcs = _pendingRebindTcs;
                _pendingRebindTcs = null;
            }

            if (tcs != null)
            {
                uint oldMod = Core.SettingsManager.Get("hotkey_modifiers", Win32Helper.MOD_CONTROL);
                uint oldVk = Core.SettingsManager.Get("hotkey_vk", Win32Helper.VK_TAB);

                Win32Helper.UnregisterHotKey(hwnd, HOTKEY_ID_TOGGLE);
                bool ok = Win32Helper.RegisterHotKey(hwnd, HOTKEY_ID_TOGGLE, newMod, newVk);
                if (!ok)
                {
                    Win32Helper.RegisterHotKey(hwnd, HOTKEY_ID_TOGGLE, oldMod, oldVk);
                }
                tcs.SetResult(ok);
            }
            return true;
        }

        return false;
    }

    public void UnregisterAll(IntPtr hwnd)
    {
        try
        {
            if (hwnd != IntPtr.Zero)
            {
                Win32Helper.UnregisterHotKey(hwnd, HOTKEY_ID_TOGGLE);
                Win32Helper.UnregisterHotKey(hwnd, HOTKEY_ID_PASTE_PLAIN);
            }
        }
        catch { }
    }
}
