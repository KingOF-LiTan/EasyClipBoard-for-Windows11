using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace clip.Native;

internal sealed class WindowMessageHook
{
    private const int GWLP_WNDPROC = -4;

    private readonly Window _window;
    private readonly IntPtr _hwnd;

    private IntPtr _oldWndProc = IntPtr.Zero;
    private WndProcDelegate? _newWndProc;

    public event Func<IntPtr, uint, IntPtr, IntPtr, bool>? Message;

    public WindowMessageHook(Window window)
    {
        _window = window;
        _hwnd = WindowNative.GetWindowHandle(_window);
    }

    public void Attach()
    {
        if (_oldWndProc != IntPtr.Zero)
        {
            return;
        }

        _newWndProc = WndProc;
        IntPtr newProcPtr = Marshal.GetFunctionPointerForDelegate(_newWndProc);
        _oldWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, newProcPtr);
    }

    public void Detach()
    {
        if (_oldWndProc == IntPtr.Zero)
        {
            return;
        }

        SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldWndProc);
        _oldWndProc = IntPtr.Zero;
        _newWndProc = null;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var handled = Message?.Invoke(hwnd, msg, wParam, lParam) ?? false;
        if (handled)
        {
            return IntPtr.Zero;
        }

        return CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
