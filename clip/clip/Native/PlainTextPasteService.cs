using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace clip.Native;

/// <summary>
/// Ctrl+Shift+V handler: read clipboard as plain Unicode, write it back
/// without formatting, restore target focus, and send synthetic Ctrl+V.
/// </summary>
internal sealed class PlainTextPasteService
{
    public void Execute(IntPtr hostHwnd)
    {
        try
        {
            IntPtr targetWindow = Win32Helper.GetForegroundWindow();
            _ = System.Threading.Tasks.Task.Run(() => PastePlainText(hostHwnd, targetWindow));
        }
        catch (Exception ex)
        {
            Log($"[PastePlain] Dispatch exception: {ex.Message}");
        }
    }

    private static void PastePlainText(IntPtr hostHwnd, IntPtr targetWindow)
    {
        try
        {
            string? plainText = null;
            if (!TryOpenClipboard(hostHwnd)) { Log("[PastePlain] Cannot open clipboard for read"); return; }
            try
            {
                IntPtr hData = Win32Helper.GetClipboardData(Win32Helper.CF_UNICODETEXT);
                if (hData != IntPtr.Zero)
                {
                    IntPtr p = Win32Helper.GlobalLock(hData);
                    if (p != IntPtr.Zero)
                    {
                        try { plainText = Marshal.PtrToStringUni(p); }
                        finally { Win32Helper.GlobalUnlock(hData); }
                    }
                }
            }
            finally { Win32Helper.CloseClipboard(); }

            if (string.IsNullOrEmpty(plainText)) { Log("[PastePlain] No CF_UNICODETEXT on clipboard"); return; }
            Log($"[PastePlain] Read {plainText.Length} chars");

            App.SuppressClipboardUpdate(TimeSpan.FromSeconds(2));

            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(plainText + "\0");
            IntPtr hGlobal = Win32Helper.GlobalAlloc(Win32Helper.GMEM_MOVEABLE, (nuint)bytes.Length);
            if (hGlobal == IntPtr.Zero) { Log("[PastePlain] GlobalAlloc failed"); return; }

            IntPtr pGlobal = Win32Helper.GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero) { Win32Helper.GlobalFree(hGlobal); return; }
            Marshal.Copy(bytes, 0, pGlobal, bytes.Length);
            Win32Helper.GlobalUnlock(hGlobal);

            if (!TryOpenClipboard(hostHwnd))
            {
                Win32Helper.GlobalFree(hGlobal);
                Log("[PastePlain] Cannot open clipboard for write");
                return;
            }
            try
            {
                Win32Helper.EmptyClipboard();
                if (Win32Helper.SetClipboardData(Win32Helper.CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                {
                    Win32Helper.GlobalFree(hGlobal);
                    Log("[PastePlain] SetClipboardData failed");
                    return;
                }
                hGlobal = IntPtr.Zero;
            }
            finally { Win32Helper.CloseClipboard(); }

            Log("[PastePlain] Clipboard updated");

            if (targetWindow != IntPtr.Zero)
            {
                Win32Helper.SetForegroundWindow(targetWindow);
                Thread.Sleep(50);
            }

            bool lShiftDown = (Win32Helper.GetAsyncKeyState(0xA0) & 0x8000) != 0;
            bool rShiftDown = (Win32Helper.GetAsyncKeyState(0xA1) & 0x8000) != 0;
            bool lCtrlDown  = (Win32Helper.GetAsyncKeyState(0xA2) & 0x8000) != 0;
            bool rCtrlDown  = (Win32Helper.GetAsyncKeyState(0xA3) & 0x8000) != 0;

            var inputs = new List<Win32Helper.INPUT>();

            if (lShiftDown) inputs.Add(KeyEvent(0xA0, Win32Helper.KEYEVENTF_KEYUP));
            if (rShiftDown) inputs.Add(KeyEvent(0xA1, Win32Helper.KEYEVENTF_KEYUP));
            if (lCtrlDown)  inputs.Add(KeyEvent(0xA2, Win32Helper.KEYEVENTF_KEYUP));
            if (rCtrlDown)  inputs.Add(KeyEvent(0xA3, Win32Helper.KEYEVENTF_KEYUP));

            inputs.Add(KeyEvent(Win32Helper.VK_SHIFT, Win32Helper.KEYEVENTF_KEYUP));
            inputs.Add(KeyEvent(Win32Helper.VK_CONTROL, Win32Helper.KEYEVENTF_KEYUP));

            inputs.Add(KeyEvent(Win32Helper.VK_CONTROL, 0));
            inputs.Add(KeyEvent(Win32Helper.VK_V, 0));
            inputs.Add(KeyEvent(Win32Helper.VK_V, Win32Helper.KEYEVENTF_KEYUP));
            inputs.Add(KeyEvent(Win32Helper.VK_CONTROL, Win32Helper.KEYEVENTF_KEYUP));

            if (lCtrlDown)  inputs.Add(KeyEvent(0xA2, 0));
            if (rCtrlDown)  inputs.Add(KeyEvent(0xA3, 0));
            if (lShiftDown) inputs.Add(KeyEvent(0xA0, 0));
            if (rShiftDown) inputs.Add(KeyEvent(0xA1, 0));

            var arr = inputs.ToArray();
            uint sent = Win32Helper.SendInput((uint)arr.Length, arr, Marshal.SizeOf<Win32Helper.INPUT>());
            Log($"[PastePlain] SendInput fired {sent}/{arr.Length} events → target=0x{targetWindow:X}");
        }
        catch (Exception ex) { Log($"[PastePlain] Exception: {ex.Message}"); }
    }

    private static bool TryOpenClipboard(IntPtr hwnd)
    {
        for (int i = 0; i < 5; i++)
        {
            if (Win32Helper.OpenClipboard(hwnd)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    private static void Log(string msg) => System.Diagnostics.Debug.WriteLine(msg);

    private static Win32Helper.INPUT KeyEvent(ushort vk, uint flags) => new()
    {
        type = Win32Helper.INPUT_KEYBOARD,
        u = new Win32Helper.INPUT_UNION
        {
            ki = new Win32Helper.KEYBDINPUT { wVk = vk, dwFlags = flags }
        }
    };
}
