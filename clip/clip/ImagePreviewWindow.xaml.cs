using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using System.IO;
using System.Threading.Tasks;

namespace clip;

public sealed partial class ImagePreviewWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly long _clipboardItemId;

    public ImagePreviewWindow(long id, string imagePath, int defaultWidth, int defaultHeight)
    {
        InitializeComponent();
        _clipboardItemId = id;

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Remove DWM border and extends into titlebar for a clean custom window
        ExtendsContentIntoTitleBar = true;
        if (AppWindow.TitleBar != null)
        {
            AppWindow.TitleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }

        // Apply dark mode and remove system borders
        ApplyImmersiveDarkMode();
        RemoveSystemBorder();

        // Presenter config: Titlebarless Overlapped
        _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        // Calculate size based on image size vs screen size
        var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        int maxWidth = (int)(workArea.Width * 0.95);
        int maxHeight = (int)(workArea.Height * 0.95);

        int targetWidth = defaultWidth > 0 ? defaultWidth : 800;
        int targetHeight = defaultHeight > 0 ? defaultHeight : 600;

        if (targetWidth > maxWidth || targetHeight > maxHeight)
        {
            float ratio = Math.Min((float)maxWidth / targetWidth, (float)maxHeight / targetHeight);
            targetWidth = (int)(targetWidth * ratio);
            targetHeight = (int)(targetHeight * ratio);
        }

        _appWindow.Resize(new Windows.Graphics.SizeInt32(targetWidth + 40, targetHeight + 40));

        // Center on screen
        int x = workArea.X + (workArea.Width - (targetWidth + 40)) / 2;
        int y = workArea.Y + (workArea.Height - (targetHeight + 40)) / 2;
        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));

        // Load Image
        try
        {
            var bmp = new BitmapImage(new Uri(imagePath));
            PreviewImage.Source = bmp;
        }
        catch { }

        // Start animation
        _ = PlayShowAnimationAsync();

        Activate();
    }

    private void Grid_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Space || e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            _ = PlayHideAndCloseAsync();
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            // Tell App to paste this item
            if (App.Current is App app)
            {
                // We shouldn't directly talk to bridge, we'll expose a static event or just call it
                app.TriggerPaste(_clipboardItemId);
            }
            _ = PlayHideAndCloseAsync();
        }
    }

    private async Task PlayShowAnimationAsync()
    {
        RootGrid.Opacity = 0;
        RootGrid.RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 0.9, ScaleY = 0.9, CenterX = _appWindow.Size.Width / 2, CenterY = _appWindow.Size.Height / 2 };
        
        // Simple async animation simulation
        for (int i = 1; i <= 10; i++)
        {
            RootGrid.Opacity = i / 10.0;
            var st = (Microsoft.UI.Xaml.Media.ScaleTransform)RootGrid.RenderTransform;
            st.ScaleX = 0.9 + (i * 0.01);
            st.ScaleY = 0.9 + (i * 0.01);
            await Task.Delay(10);
        }
        RootGrid.Opacity = 1;
        RootGrid.RenderTransform = null;
    }

    private async Task PlayHideAndCloseAsync()
    {
        RootGrid.RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 1.0, ScaleY = 1.0, CenterX = _appWindow.Size.Width / 2, CenterY = _appWindow.Size.Height / 2 };
        for (int i = 10; i >= 1; i--)
        {
            RootGrid.Opacity = i / 10.0;
            var st = (Microsoft.UI.Xaml.Media.ScaleTransform)RootGrid.RenderTransform;
            st.ScaleX = 0.9 + (i * 0.01);
            st.ScaleY = 0.9 + (i * 0.01);
            await Task.Delay(10);
        }
        Close();
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Focus the grid so we handle key events
        RootGrid.Focus(FocusState.Programmatic);
    }

    private void ApplyImmersiveDarkMode()
    {
        try
        {
            int val = 1;
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
            int cornerPref = Native.Win32Helper.DWMWCP_DONOTROUND;
            Native.Win32Helper.DwmSetWindowAttribute(_hwnd, Native.Win32Helper.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
        }
        catch { }
    }
}
