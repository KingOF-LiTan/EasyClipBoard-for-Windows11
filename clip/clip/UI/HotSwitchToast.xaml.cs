using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Microsoft.UI;
using clip.Native;

namespace clip.UI;

public sealed partial class HotSwitchToast : Window
{
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly OverlappedPresenter? _presenter;
    private readonly DispatcherTimer _hideTimer;

    private const int ToastWidth = 280;
    private const int ToastHeight = 48;

    public HotSwitchToast()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // 彻底移除标题栏和系统按钮
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        _presenter = _appWindow.Presenter as OverlappedPresenter;
        if (_presenter != null)
        {
            _presenter.IsResizable = false;
            _presenter.IsMinimizable = false;
            _presenter.IsMaximizable = false;
            _presenter.SetBorderAndTitleBar(false, false);
            _presenter.IsAlwaysOnTop = true;
            _appWindow.IsShownInSwitchers = false;
        }

        // 设置小尺寸（避免默认大窗口）
        _appWindow.Resize(new Windows.Graphics.SizeInt32(ToastWidth, ToastHeight));

        // 设置不占焦点 (WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT to click-through)
        var currentExStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE,
            currentExStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            ShowWindow(_hwnd, SW_HIDE);
        };
    }

    public void ShowPreview(string text, int x, int y)
    {
        PreviewText.Text = text;

        // 定位到鼠标附近
        _appWindow.Resize(new Windows.Graphics.SizeInt32(ToastWidth, ToastHeight));
        _appWindow.Move(new Windows.Graphics.PointInt32(x + 20, y - ToastHeight - 10));

        // 使用 SW_SHOWNOACTIVATE 避免抢焦点
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

        ShowStory.Begin();
        HideStory.Begin();

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    // ── Win32 常量 & PInvoke ──
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_HIDE = 0;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
