using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;
using clip.Core.Models;
using clip.Core.Storage;
using clip.ViewModels;

namespace clip;

public sealed partial class MainWindow : Window, System.ComponentModel.INotifyPropertyChanged
{
    private const int DefaultDipWidth = 360;
    private const int DefaultDipHeight = 520;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly OverlappedPresenter? _presenter;

    // ── 拖拽状态 ──
    private bool _isDragging;
    private Windows.Graphics.PointInt32 _dragStartWindowPos;
    private int _dragStartCursorX, _dragStartCursorY;

    private MainViewModel _viewModel = null!;
    public MainViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel != value)
            {
                _viewModel = value;
                OnPropertyChanged();
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public bool IsShowing { get; private set; } = false;


    public MainWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _presenter = _appWindow.Presenter as OverlappedPresenter;
        if (_presenter != null)
        {
            _presenter.IsResizable = false;
            _presenter.SetBorderAndTitleBar(false, false);
            // SetBorderAndTitleBar 可能重置置顶，所以放在最后
            _presenter.IsAlwaysOnTop = true;
        }

        // 使用 WinUI 3 标准方案实现无边框
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        // 初始收紧尺寸
        _appWindow.Resize(new Windows.Graphics.SizeInt32(DefaultDipWidth, DefaultDipHeight));

        // 注册拖拽事件（无标题栏时通过内容区拖拽窗口）
        var root = (UIElement)this.Content;
        root.PointerPressed += DragRegion_PointerPressed;
        root.PointerMoved += DragRegion_PointerMoved;
        root.PointerReleased += DragRegion_PointerReleased;

        Closed += (s, e) => { };
    }

    public void Initialize(StorageService storage, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcher, Action onBeforeClipboardWrite)
    {
        ViewModel = new MainViewModel(storage, uiDispatcher)
        {
            OnBeforeClipboardWrite = onBeforeClipboardWrite
        };
        ItemListView.ItemsSource = ViewModel.Items;
    }

    public void ToggleVisibility() => ShowWindowAt(0, 0);

    public async void ShowWindowAt(int mouseX, int mouseY)
    {
        var pt = new Native.Win32Helper.POINT { X = mouseX, Y = mouseY };
        var hMonitor = Native.Win32Helper.MonitorFromPoint(pt, Native.Win32Helper.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.Win32Helper.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.Win32Helper.MONITORINFO>() };
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
        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));

        Activate();
        IsShowing = true;

        // 重新确认置顶
        if (_presenter != null) _presenter.IsAlwaysOnTop = true;
        // 强制置顶 (HWND_TOPMOST)
        Native.Win32Helper.SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0003); // TOPMOST, NOSIZE, NOMOVE

        // 弹性下滑动画
        RootTransform.TranslateY = -40;
        SlideDownStory.Begin();

        if (ViewModel != null) await ViewModel.RefreshAsync();
    }

    public async Task RefreshList()
    {
        if (ViewModel != null) await ViewModel.RefreshAsync();
    }

    public void HideWindow()
    {
        ContentBorder.Opacity = 0;
        _appWindow.Hide();
        IsShowing = false;
    }

    private async void BtnHistory_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.ShowFavorites = false;
        BtnHistory.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        BtnFavorites.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
        await ViewModel.RefreshAsync();
    }

    private async void BtnFavorites_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        
        // 点击收藏按钮时，如果隐形面板开着，则关闭
        if (SecretPanel.Visibility == Visibility.Visible)
        {
            SecretPanel.Visibility = Visibility.Collapsed;
        }

        ViewModel.ShowFavorites = true;
        BtnFavorites.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        BtnHistory.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
        await ViewModel.RefreshAsync();
    }

    private async void BtnFavorites_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // 隐形入口：右键点击收藏夹按钮，打开/关闭敏感信息面板
        if (SecretPanel.Visibility == Visibility.Collapsed)
        {
            SecretPanel.Visibility = Visibility.Visible;
            if (ViewModel != null) await ViewModel.RefreshSensitiveAsync();
        }
        else
        {
            SecretPanel.Visibility = Visibility.Collapsed;
        }
    }

    // ── 敏感信息保险箱事件 ──

    private async void SecretAdd_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var panel = new StackPanel { Spacing = 12 };
        var aliasBox = new TextBox { PlaceholderText = "别名/标题 (如: GitHub Token)", Height = 32 };
        var userBox = new TextBox { PlaceholderText = "账号/用户名 (若无可不填)", Height = 32 };
        var contentBox = new PasswordBox { PlaceholderText = "密码/Key/连接串", Height = 32 };
        var typeCombo = new ComboBox 
        { 
            PlaceholderText = "类型", 
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            // 移除单独的"密码"项，由"账号密码"统一管理
            Items = { "账号密码", "API Key", "私钥", "连接串", "其他" },
            SelectedIndex = 0
        };
        panel.Children.Add(aliasBox);
        panel.Children.Add(userBox);
        panel.Children.Add(contentBox);
        panel.Children.Add(typeCombo);

        var dialog = new ContentDialog
        {
            XamlRoot = this.Content.XamlRoot,
            Title = "➕ 添加敏感信息",
            Content = panel,
            PrimaryButtonText = "加密保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary 
            && (!string.IsNullOrWhiteSpace(aliasBox.Text) || !string.IsNullOrEmpty(contentBox.Password)))
        {
            var stype = typeCombo.SelectedIndex switch
            {
                0 => Core.Models.SensitiveType.Credential,
                1 => Core.Models.SensitiveType.ApiKey,
                2 => Core.Models.SensitiveType.PrivateKey,
                3 => Core.Models.SensitiveType.ConnectionString,
                _ => Core.Models.SensitiveType.Other
            };
            // 如果仅填写了密码没填别名，尝试用部分密码做别名（不推荐但防止空别名逻辑）
            var finalAlias = string.IsNullOrWhiteSpace(aliasBox.Text) ? "未命名项" : aliasBox.Text;
            
            await ViewModel.AddManualSecretAsync(finalAlias, contentBox.Password, stype, userBox.Text);
        }
    }

    private void SecretClose_Click(object sender, RoutedEventArgs e)
    {
        SecretPanel.Visibility = Visibility.Collapsed;
    }

    private async void SecretSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ViewModel == null) return;
        await ViewModel.RefreshSensitiveAsync(SecretSearchBox.Text);
    }

    private async void SecretCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = GetViewModelFromSender(sender);
        if (vm == null) return;

        var decrypted = await ViewModel.GetDecryptedTextAsync(vm.Id);
        if (string.IsNullOrEmpty(decrypted)) return;

        // 如果是账号密码类型且有账号，为了配合HotSwitch流水线粘贴，需將账号密码依次压入剪贴板
        // 顺序：先压密码（历史），再压账号（当前）。用户粘贴账号后，Trigger HotSwitch 粘贴密码。
        if (vm.SensitiveType == Core.Models.SensitiveType.Credential && !string.IsNullOrEmpty(vm.Username))
        {
            try
            {
                var dpPass = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dpPass.SetText(decrypted);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dpPass);
                Windows.ApplicationModel.DataTransfer.Clipboard.Flush(); // 确保写入历史
                
                await Task.Delay(150); // 给系统一点时间处理历史记录

                var dpUser = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dpUser.SetText(vm.Username);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dpUser);
                Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            }
            catch { }
        }
        else
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(decrypted);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        }
    }

    private void SecretCopyUser_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModelFromSender(sender);
        if (vm == null || string.IsNullOrEmpty(vm.Username)) return;

        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(vm.Username);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
    }

    private void SecretToggleView_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModelFromSender(sender);
        if (vm == null) return;
        vm.IsDecrypted = !vm.IsDecrypted;
    }

    private async void SecretDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = GetViewModelFromSender(sender);
        if (vm == null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = this.Content.XamlRoot,
            Title = "确认删除",
            Content = $"确定删除 \"{vm.DisplayName}\" 吗？此操作不可恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteSecretAsync(vm);
        }
    }

    private async void ItemListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel == null) return;
        
        var vm = e.ClickedItem as ClipboardItemViewModel;
        if (vm == null) return;

        // Ctrl 多选逻辑
        var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        
        if (ctrlPressed)
        {
            // Toggle 选中状态
            vm.IsSelected = !vm.IsSelected;
            ViewModel.UpdateSelectionState();
        }
        else
        {
            // 普通点击：
            // 如果已经有选中的项，点击其他项则取消所有选中
            if (ViewModel.HasSelection)
            {
                ViewModel.ClearSelection();
            }
            // 正常点击粘贴
            else
            {
                await ViewModel.PasteItemAsync(vm);
                HideWindow();
            }
        }
    }

    private void ItemGrid_RightTapped(object sender, RightTappedRoutedEventArgs e) { }

    private async void MenuFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = GetViewModelFromSender(sender);
        if (vm != null) await ViewModel.UpdateTagAsync(vm, ClipboardItemTag.Important);
    }

    private async void MenuFrequent_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = GetViewModelFromSender(sender);
        if (vm != null) await ViewModel.UpdateTagAsync(vm, ClipboardItemTag.Frequent);
    }

    private async void MenuScript_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = GetViewModelFromSender(sender);
        if (vm != null) await ViewModel.UpdateTagAsync(vm, ClipboardItemTag.Script);
    }

    private async void MenuTemporary_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = GetViewModelFromSender(sender);
        if (vm != null) await ViewModel.UpdateTagAsync(vm, ClipboardItemTag.Temporary);
    }

    private async void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = GetViewModelFromSender(sender);
        if (vm != null) await ViewModel.DeleteItemAsync(vm);
    }

    private async void MenuAddToFavorites_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = GetViewModelFromSender(sender);
        if (vm != null) await ViewModel.ToggleFavoriteAsync(vm);
    }

    private async void MenuRename_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = GetViewModelFromSender(sender);
        if (vm == null) return;

        var inputTextBox = new TextBox 
        { 
            AcceptsReturn = false, 
            Height = 32, 
            Text = vm.Alias ?? "",
            PlaceholderText = "输入别名..."
        };
        
        var dialog = new ContentDialog
        {
            XamlRoot = this.Content.XamlRoot,
            Title = "设置别名/备注",
            Content = inputTextBox,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            vm.Alias = inputTextBox.Text;
        }
    }

    private static ClipboardItemViewModel? GetViewModelFromSender(object sender)
    {
        if (sender is Button btn && btn.DataContext is ClipboardItemViewModel vm) return vm;
        if (sender is MenuFlyoutItem item && item.DataContext is ClipboardItemViewModel vm2) return vm2;
        return null;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ViewModel != null) ViewModel.SearchText = SearchBox.Text;
    }

    private async void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog confirmDialog = new ContentDialog
        {
            XamlRoot = this.Content.XamlRoot,
            Title = "确认清空历史？",
            Content = "这将删除所有非收藏的剪贴板记录，且无法恢复。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await confirmDialog.ShowAsync();
        if (result == ContentDialogResult.Primary && ViewModel != null)
        {
            await ViewModel.ClearAllHistoryAsync();
        }
    }

    // ── 批量操作 ──

    private async void BatchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        await ViewModel.DeleteSelectedAsync();
    }

    private async void BatchFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        await ViewModel.FavoriteSelectedAsync();
    }

    private void BatchCancel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.ClearSelection();
    }

    // ── 窗口拖拽（手动追踪方式，不阻塞 WinUI 消息循环） ──
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
