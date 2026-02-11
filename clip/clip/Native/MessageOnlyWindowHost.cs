using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace clip.Native;

internal sealed class MessageOnlyWindowHost : IDisposable
{
    private const int WM_DESTROY = 0x0002;
    private const int WM_COMMAND = 0x0111;
    private const int WM_HOTKEY = 0x0312;

    private const int ID_TRAY_SHOWHIDE = 2001;
    private const int ID_TRAY_SETTINGS = 2002;
    private const int ID_TRAY_EXIT = 2003;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;

    private readonly DispatcherQueue _uiQueue;

    private Thread? _thread;
    private IntPtr _hwnd;

    private TrayIconService? _tray;
    private IntPtr _menu;

    public event Action<int, int>? ToggleUiRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? ClipboardChanged;

    public MessageOnlyWindowHost(DispatcherQueue uiQueue)
    {
        _uiQueue = uiQueue;
    }

    public void Start()
    {
        if (_thread != null)
        {
            return;
        }

        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "WinClipboard.Win32Host"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread == null)
        {
            return;
        }

        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
        }

        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private void ThreadMain()
    {
        _instance = this;

        _hwnd = CreateMessageOnlyWindow();
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // hotkey
        Win32Helper.RegisterHotKey(_hwnd, 1001, Win32Helper.MOD_CONTROL, Win32Helper.VK_TAB);

        // clipboard listener
        Win32Helper.AddClipboardFormatListener(_hwnd);

        // tray
        _tray = new TrayIconService(_hwnd);
        _tray.LeftClick += () => _uiQueue.TryEnqueue(() => RaiseToggleUiAtCursor());
        _tray.RightClick += ShowTrayMenu;

        IntPtr hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
        _tray.Create(hIcon, "WinClipboard");

        // menu
        _menu = CreatePopupMenu();
        AppendMenu(_menu, 0x0000, ID_TRAY_SHOWHIDE, "显示/隐藏");
        AppendMenu(_menu, 0x0000, ID_TRAY_SETTINGS, "设置");
        AppendMenu(_menu, 0x0000, ID_TRAY_EXIT, "退出");

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        Cleanup();
    }

    private void Cleanup()
    {
        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                Win32Helper.RemoveClipboardFormatListener(_hwnd);
                Win32Helper.UnregisterHotKey(_hwnd, 1001);
            }
        }
        catch
        {
        }

        try
        {
            _tray?.Dispose();
        }
        catch
        {
        }

        try
        {
            if (_menu != IntPtr.Zero)
            {
                DestroyMenu(_menu);
                _menu = IntPtr.Zero;
            }
        }
        catch
        {
        }

        _hwnd = IntPtr.Zero;
        _instance = null;
    }

    private void RaiseToggleUiAtCursor()
    {
        if (GetCursorPos(out var pt))
        {
            ToggleUiRequested?.Invoke(pt.X, pt.Y);
        }
        else
        {
            ToggleUiRequested?.Invoke(0, 0);
        }
    }

    private void ShowTrayMenu()
    {
        if (_menu == IntPtr.Zero || _hwnd == IntPtr.Zero)
        {
            return;
        }

        GetCursorPos(out var pt);

        // Required so the menu closes correctly.
        SetForegroundWindow(_hwnd);

        int cmd = TrackPopupMenuEx(
            _menu,
            TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_RETURNCMD,
            pt.X,
            pt.Y,
            _hwnd,
            IntPtr.Zero);

        if (cmd != 0)
        {
            // Simulate WM_COMMAND to reuse handler.
            PostMessage(_hwnd, WM_COMMAND, new IntPtr(cmd), IntPtr.Zero);
        }

        PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr CreateMessageOnlyWindow()
    {
        _wndProcDelegate = WndProc;

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            lpszClassName = "WinClipboard.MessageOnlyWindow"
        };

        RegisterClassEx(ref wc);

        // HWND_MESSAGE = (HWND)-3
        return CreateWindowEx(
            0,
            wc.lpszClassName,
            "",
            0,
            0,
            0,
            0,
            0,
            new IntPtr(-3),
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private static MessageOnlyWindowHost? _instance;
    private static WndProcDelegate? _wndProcDelegate;

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var host = _instance;
        if (host == null)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        if (msg == Win32Helper.WM_CLIPBOARDUPDATE)
        {
            host._uiQueue.TryEnqueue(() => host.ClipboardChanged?.Invoke());
            return IntPtr.Zero;
        }

        if (msg == WM_HOTKEY)
        {
            if (wParam.ToInt32() == 1001)
            {
                host._uiQueue.TryEnqueue(() => host.RaiseToggleUiAtCursor());
                return IntPtr.Zero;
            }
        }

        // Let TrayIconService handle WM_TRAYICON
        if (host._tray?.HandleMessage(msg, wParam, lParam) == true)
        {
            return IntPtr.Zero;
        }

        if (msg == WM_COMMAND)
        {
            int id = wParam.ToInt32() & 0xFFFF;
            if (id == ID_TRAY_SHOWHIDE)
            {
                host._uiQueue.TryEnqueue(() => host.RaiseToggleUiAtCursor());
            }
            else if (id == ID_TRAY_SETTINGS)
            {
                host._uiQueue.TryEnqueue(() => host.SettingsRequested?.Invoke());
            }
            else if (id == ID_TRAY_EXIT)
            {
                host._uiQueue.TryEnqueue(() => host.ExitRequested?.Invoke());
            }

            return IntPtr.Zero;
        }

        if (msg == WM_DESTROY)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    #region Win32

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Win32Helper.POINT pt;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Helper.POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    #endregion
}
