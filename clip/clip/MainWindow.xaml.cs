using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;
using WinRT.Interop;
using clip.Core.Storage;

namespace clip;

public sealed partial class MainWindow : Window
{
    // ── Window management ──
    private const int DefaultDipWidth = 380;
    private const int DefaultDipHeight = 560;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly OverlappedPresenter? _presenter;

    // ── Drag state ──
    private bool _isDragging;
    private Windows.Graphics.PointInt32 _dragStartWindowPos;
    private int _dragStartCursorX, _dragStartCursorY;

    // ── Backend ──
    private StorageService _storage = null!;
    private WebBridge? _bridge;

    public bool IsShowing { get; private set; } = false;
    public new static MainWindow? Current { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        Current = this;

        // DWM: immersive dark mode
        ApplyImmersiveDarkMode();

        // Extend content into title bar to remove system chrome
        ExtendsContentIntoTitleBar = true;

        // Enable Native Windows 11 SystemBackdrop (MicaAlt / Acrylic)
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
            var mica = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            try { mica.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt; } catch { }
            SystemBackdrop = mica;
        }
        else if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        }

        // Must be transparent to let the native backdrop shine through
        RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

        if (AppWindow.TitleBar != null)
        {
            AppWindow.TitleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }

        // Set presenter: borderless, always on top
        _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        _presenter = _appWindow.Presenter as OverlappedPresenter;
        if (_presenter != null)
        {
            _presenter.IsResizable = false;
            _presenter.SetBorderAndTitleBar(false, false);
            _presenter.IsAlwaysOnTop = true;
            _presenter.IsMaximizable = false;
            _presenter.IsMinimizable = false;
        }

        // Remove DWM border
        RemoveSystemBorder();

        // Initial size
        _appWindow.Resize(new Windows.Graphics.SizeInt32(DefaultDipWidth, DefaultDipHeight));

        Closed += (s, e) => { };

        // Ensure WebView2 inherently supports transparency at the environment level
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00000000");

        // Initialize WebView2
        _ = InitWebViewAsync();
    }

    // ── WebView2 Initialization ──

    private async Task InitWebViewAsync()
    {
        try
        {
            string userDataDir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "WebView2");
            var env = await CoreWebView2Environment.CreateAsync();

            await WebView.EnsureCoreWebView2Async(env);

            // Transparent background
            WebView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            // Settings
            var settings = WebView.CoreWebView2.Settings;
            settings.IsStatusBarEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;

            // Virtual host mapping: map 'app.local' to wwwroot folder
            string wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);

            // Clear cache locally to ensure UI updates are fetched
            await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync();

            // Removing previous data.local mapping as it causes CORS issues
            
            // Message handler
            WebView.CoreWebView2.WebMessageReceived += async (s, e) =>
            {
                if (_bridge != null)
                {
                    await _bridge.HandleMessageAsync(e.WebMessageAsJson);
                }
            };

            // Navigate
            WebView.CoreWebView2.Navigate("https://app.local/index.html");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebView2] Init failed: {ex.Message}");
        }
    }

    // ── Public API (called from App.xaml.cs) ──

    public void Initialize(StorageService storage, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcher, Action onBeforeClipboardWrite)
    {
        _storage = storage;
        _bridge = new WebBridge(storage, onBeforeClipboardWrite, async (script) =>
        {
            try
            {
                if (WebView?.CoreWebView2 != null)
                    await WebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch { }
        });
    }

    public void ToggleVisibility() => ShowWindowAt(0, 0);

    public async void ShowWindowAt(int mouseX, int mouseY)
    {
        var pt = new Native.Win32Helper.POINT { X = mouseX, Y = mouseY };
        var hMonitor = Native.Win32Helper.MonitorFromPoint(pt, Native.Win32Helper.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.Win32Helper.MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.Win32Helper.MONITORINFO>()
        };
        Native.Win32Helper.GetMonitorInfo(hMonitor, ref mi);

        double scale = Native.Win32Helper.GetScaleForMonitor(hMonitor);
        int windowWidthPx = (int)Math.Ceiling(DefaultDipWidth * scale);
        int windowHeightPx = (int)Math.Ceiling(DefaultDipHeight * scale);

        int x = mouseX - windowWidthPx / 2;
        int y = mouseY - 20;

        var work = mi.rcWork;
        const int margin = 10;

        if (x + windowWidthPx + margin > work.Right) x = mouseX - windowWidthPx - margin;
        if (x < work.Left + margin) x = mouseX + margin;
        if (y + windowHeightPx + margin > work.Bottom) y = work.Bottom - windowHeightPx - margin;
        if (y < work.Top + margin) y = work.Top + margin;

        _appWindow.Resize(new Windows.Graphics.SizeInt32(windowWidthPx, windowHeightPx));

        if (_presenter != null)
        {
            _presenter.SetBorderAndTitleBar(false, false);
            _presenter.IsResizable = false;
        }
        RemoveSystemBorder();

        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));

        if (!IsShowing)
        {
            _appWindow.Show();
            IsShowing = true;
            Native.Win32Helper.SetForegroundWindow(_hwnd);
        }

        // Trigger show animation and notify frontend to refresh
        if (_bridge != null)
        {
            await _bridge.PushClipboardUpdateAsync();
            if (WebView.CoreWebView2 != null)
            {
                WebView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                try { await WebView.CoreWebView2.ExecuteScriptAsync("window.focus(); document.body.focus(); window.__on_window_shown();"); } catch { }
            }
        }
    }

    public async void HideWindow()
    {
        if (!IsShowing) return;
        IsShowing = false;
        
        try
        {
            // Trigger hide animation in JS, JS will call hideWindow bridge method when done
            if (WebView.CoreWebView2 != null)
            {
                await WebView.CoreWebView2.ExecuteScriptAsync("window.hideWindowAnimated()");
            }
            else
            {
                _appWindow.Hide();
            }
        }
        catch
        {
            _appWindow.Hide();
        }
    }
    
    // Called by WebBridge when the animation finishes
    public void HideWindowImmediate()
    {
        IsShowing = false;
        _appWindow.Hide();
    }

    public async void NotifyClipboardChanged()
    {
        if (_bridge != null)
        {
            await _bridge.PushClipboardUpdateAsync();
        }
    }

    // ── DWM helpers ──

    public void ApplyTheme(int theme)
    {
        bool isDark = theme switch
        {
            1 => true,
            2 => false,
            _ => App.Current.RequestedTheme == ApplicationTheme.Dark
        };

        try
        {
            int val = isDark ? 1 : 0;
            Native.Win32Helper.DwmSetWindowAttribute(_hwnd, Native.Win32Helper.DWMWA_USE_IMMERSIVE_DARK_MODE_20, ref val, sizeof(int));
            Native.Win32Helper.DwmSetWindowAttribute(_hwnd, Native.Win32Helper.DWMWA_USE_IMMERSIVE_DARK_MODE_19, ref val, sizeof(int));
        }
        catch { }
    }

    private void ApplyImmersiveDarkMode()
    {
        try
        {
            bool isDark = true; // Default to dark
            int val = isDark ? 1 : 0;
            Native.Win32Helper.DwmSetWindowAttribute(_hwnd, Native.Win32Helper.DWMWA_USE_IMMERSIVE_DARK_MODE_20, ref val, sizeof(int));
            Native.Win32Helper.DwmSetWindowAttribute(_hwnd, Native.Win32Helper.DWMWA_USE_IMMERSIVE_DARK_MODE_19, ref val, sizeof(int));
        }
        catch { }
    }

    private void RemoveSystemBorder()
    {
        try
        {
            uint color = Native.Win32Helper.DWM_BORDER_COLOR_NONE;
            Native.Win32Helper.DwmSetWindowAttribute(_hwnd, Native.Win32Helper.DWMWA_BORDER_COLOR, ref color, sizeof(uint));

            // Enable Windows 11 auto-rounded corners
            int cornerPref = Native.Win32Helper.DWMWCP_ROUND;
            Native.Win32Helper.DwmSetWindowAttribute(_hwnd, Native.Win32Helper.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
        }
        catch { }
    }

    // ── Drag handlers ──

    private void DragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        _isDragging = true;
        _dragStartWindowPos = _appWindow.Position;
        Native.Win32Helper.GetCursorPos(out var pt);
        _dragStartCursorX = pt.X;
        _dragStartCursorY = pt.Y;
        ((UIElement)sender).CapturePointer(e.Pointer);
    }

    private void DragRegion_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        Native.Win32Helper.GetCursorPos(out var pt);
        int dx = pt.X - _dragStartCursorX;
        int dy = pt.Y - _dragStartCursorY;
        _appWindow.Move(new Windows.Graphics.PointInt32(
            _dragStartWindowPos.X + dx,
            _dragStartWindowPos.Y + dy));
    }

    private void DragRegion_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
    }
}
