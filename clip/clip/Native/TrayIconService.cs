using System;
using System.Runtime.InteropServices;

namespace clip.Native;

internal sealed class TrayIconService : IDisposable
{
    private const int WM_APP = 0x8000;
    private const int WM_TRAYICON = WM_APP + 1;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private readonly IntPtr _hwnd;
    private readonly uint _uID;

    private bool _created;

    public event Action? LeftClick;
    public event Action? RightClick;

    public TrayIconService(IntPtr hwnd, uint iconId = 1)
    {
        _hwnd = hwnd;
        _uID = iconId;
    }

    public void Create(IntPtr hIcon, string tooltip)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _uID,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = tooltip
        };

        Shell_NotifyIcon(NIM_ADD, ref data);
        _created = true;
    }

    public bool HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WM_TRAYICON)
        {
            return false;
        }

        int mouseMsg = lParam.ToInt32();
        if (mouseMsg == WM_LBUTTONUP)
        {
            LeftClick?.Invoke();
        }
        else if (mouseMsg == WM_RBUTTONUP)
        {
            RightClick?.Invoke();
        }

        return true;
    }

    public void Dispose()
    {
        if (!_created)
        {
            return;
        }

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _uID
        };

        Shell_NotifyIcon(NIM_DELETE, ref data);
        _created = false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public IntPtr hBalloonIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
}
