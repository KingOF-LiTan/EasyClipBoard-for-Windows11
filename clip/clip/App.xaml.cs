using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using clip.Core.Storage;
using clip.Native;

namespace clip
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private MessageOnlyWindowHost? _host;
        private MainWindow? _mainWindow;
        private Window? _dialogOwner;
        private StorageService? _storage;
        private DispatcherTimer? _purgeTimer;
        private DispatcherQueue? _uiDispatcher;
        private bool _isInitialized;

        // ── 热切换 (Hot Switch) 状态 ──
        private KeyboardHookService? _kbdHook;
        private DateTimeOffset _lastVPressTime = DateTimeOffset.MinValue;
        private int _hotSwitchIndex = 0;
        private System.Collections.Generic.List<ClipboardItemEntity>? _hotSwitchCache;
        private TimeSpan _hotSwitchWindow = TimeSpan.FromMilliseconds(1500);
        private UI.HotSwitchToast? _hotSwitchToast;

        // ── 去重逻辑 ──
        private string? _lastFingerprint;
        private DateTimeOffset _lastCaptureTime = DateTimeOffset.MinValue;
        private static readonly TimeSpan DedupeWindow = TimeSpan.FromMilliseconds(1000);

        /// <summary>
        /// 自触发抑制：写入剪贴板后 500ms 内忽略 ClipboardChanged 事件。
        /// </summary>
        private DateTimeOffset _suppressUntil = DateTimeOffset.MinValue;
        private static readonly TimeSpan SuppressWindow = TimeSpan.FromMilliseconds(500);

        public App()
        {
            InitializeComponent();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _uiDispatcher = DispatcherQueue.GetForCurrentThread();

            try
            {
                // ── 初始化存储层 ──
                _storage = new StorageService();
                await _storage.InitAsync();
                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("[App] 存储层初始化成功");

                // ── 启动时清理 ──
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var n = await _storage.PurgeExpiredAsync(TimeSpan.FromDays(3));
                    if (n > 0) System.Diagnostics.Debug.WriteLine($"[Storage] 启动清理: {n}条记录");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] 初始化严重失败: {ex.Message}");
            }

            // ── 定时清理 ──
            _purgeTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _purgeTimer.Tick += async (s, e) =>
            {
                if (_isInitialized && _storage != null)
                {
                    var n = await _storage.PurgeExpiredAsync(TimeSpan.FromDays(3));
                    if (n > 0) System.Diagnostics.Debug.WriteLine($"[Storage] 定时清理: {n}条记录");
                }
            };
            _purgeTimer.Start();

            // ── 消息窗口 Host ──
            _host = new MessageOnlyWindowHost(_uiDispatcher!);
            _host.ToggleUiRequested += (x, y) => ToggleMainWindow(x, y);
            _host.SettingsRequested += ShowSettings;
            _host.ExitRequested += async () => await ConfirmExitAsync();
            _host.ClipboardChanged += async () => await OnClipboardChangedAsync();
            _host.Start();

            // ── 初始化键盘钩子 (Hot Switch) ──
            _kbdHook = new KeyboardHookService();
            _kbdHook.CtrlVPressed += OnHotSwitchPressed;
            _kbdHook.Start();
        }

        /// <summary>
        /// 热切换核心逻辑。在 hook 回调中同步替换剪贴板内容，return false 让系统自然粘贴。
        /// 不拦截按键，避免需要模拟键盘输入。
        /// </summary>
        private bool OnHotSwitchPressed()
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = now - _lastVPressTime;

            if (elapsed > _hotSwitchWindow)
            {
                // ── 首次按下（或超出时间窗口）：重置，不替换 ──
                _hotSwitchIndex = 0;
                _lastVPressTime = now;

                // 异步预加载历史缓存供后续连按使用
                _uiDispatcher?.TryEnqueue(async () =>
                {
                    if (_storage != null)
                        _hotSwitchCache = await _storage.GetHistoryAsync(20);
                });

                return false; // 让系统正常粘贴
            }

            // ── 自适应时间窗口：根据用户连按节奏调整 ──
            if (_hotSwitchIndex == 0 && elapsed.TotalMilliseconds > 80)
            {
                // 首次连按间隔 × 2.5 作为后续判定窗口
                _hotSwitchWindow = TimeSpan.FromMilliseconds(
                    Math.Clamp(elapsed.TotalMilliseconds * 2.5, 500, 3000));
                System.Diagnostics.Debug.WriteLine($"[HotSwitch] 自适应窗口: {_hotSwitchWindow.TotalMilliseconds:F0}ms");
            }

            _lastVPressTime = now;
            _hotSwitchIndex++;

            // ── 检查缓存 ──
            if (_hotSwitchCache == null || _hotSwitchCache.Count == 0)
                return false;

            // 循环索引
            if (_hotSwitchIndex >= _hotSwitchCache.Count)
                _hotSwitchIndex = 0;

            var item = _hotSwitchCache[_hotSwitchIndex];

            // 目前仅支持文本热切换
            if (item.Type != Core.Models.ClipboardItemType.Text || string.IsNullOrEmpty(item.TextContent))
                return false;

            try
            {
                // ── 同步替换剪贴板内容（在 hook 返回前完成） ──
                string pasteText = item.TextContent;
                if (item.IsSensitive)
                {
                    pasteText = Core.Security.EncryptionService.Decrypt(pasteText);
                }

                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(pasteText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                Windows.ApplicationModel.DataTransfer.Clipboard.Flush();

                // ── 异步显示 Toast 反馈 ──
                _uiDispatcher?.TryEnqueue(() =>
                {
                    try
                    {
                        _hotSwitchToast ??= new UI.HotSwitchToast();
                        Win32Helper.GetCursorPos(out var pt);
                        string preview = pasteText; // Use decrypted text
                        if (preview.Length > 25) preview = preview[..25] + "…";
                        // 遮蔽敏感信息预览 (optional design choice, user asked to remove "initialization of a bunch of preview information", maybe they prefer no preview or a simple one?)
                        if (item.IsSensitive) preview = "🔒 [敏感信息]";
                        _hotSwitchToast.ShowPreview($"[{_hotSwitchIndex + 1}] {preview}", pt.X, pt.Y);
                    }
                    catch (Exception) { }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HotSwitch] 剪贴板替换失败: {ex.Message}");
            }

            return false; // 不拦截，让系统粘贴已替换的内容
        }

        private void ToggleMainWindow(int x, int y)
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                if (_storage != null)
                {
                    _mainWindow.Initialize(_storage, _uiDispatcher!, SetSuppressFlag);
                }
            }
            if (_mainWindow.IsShowing)
            {
                _mainWindow.HideWindow();
            }
            else
            {
                _mainWindow.ShowWindowAt(x, y);
            }
        }

        private void ShowSettings()
        {
            Win32Helper.GetCursorPos(out var pt);
            ToggleMainWindow(pt.X, pt.Y);
        }

        /// <summary>
        /// 写入剪贴板前调用，设置抑制标志。
        /// </summary>
        private void SetSuppressFlag()
        {
            _suppressUntil = DateTimeOffset.UtcNow + SuppressWindow;
        }

        private async System.Threading.Tasks.Task ConfirmExitAsync()
        {
            _dialogOwner ??= new Window();
            // 确保 Content 不为 null，以激活 XamlRoot
            _dialogOwner.Content ??= new Grid { Width = 0, Height = 0 };
            _dialogOwner.Activate();

            var dialog = new ContentDialog
            {
                Title = "退出 WinClipboard",
                Content = "确定要退出吗？后台剪贴板记录将停止。",
                PrimaryButtonText = "退出",
                CloseButtonText = "取消",
                XamlRoot = (_dialogOwner.Content as FrameworkElement)?.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _purgeTimer?.Stop();
                _host?.Stop();
                _storage?.Dispose();
                Application.Current.Exit();
            }
        }

        private async System.Threading.Tasks.Task OnClipboardChangedAsync()
        {
            if (!_isInitialized || _storage == null)
            {
                System.Diagnostics.Debug.WriteLine("[Clipboard] 存储层未就绪，跳过");
                return;
            }

            // ── 检查抑制标志 ──
            if (DateTimeOffset.UtcNow < _suppressUntil)
            {
                System.Diagnostics.Debug.WriteLine("[Clipboard] 抑制自触发更新");
                return;
            }

            try
            {
                var draft = await Core.Clipboard.ClipboardReader.ReadAsync();
                if (draft == null) return;

                // ── 智能识别账号密码 ──
                bool isSensitive = false;
                if (draft.Type == Core.Models.ClipboardItemType.Text && !string.IsNullOrEmpty(draft.Text))
                {
                    isSensitive = IdentifySensitiveContent(draft.Text);
                }

                // ── 1000ms 窗口去重 ──
                var fingerprint = BuildFingerprint(draft);
                var now = DateTimeOffset.UtcNow;
                if (_lastFingerprint == fingerprint && (now - _lastCaptureTime) < DedupeWindow)
                {
                    System.Diagnostics.Debug.WriteLine("[Clipboard] 命中去重窗口，跳过重复条目");
                    return;
                }
                _lastFingerprint = fingerprint;
                _lastCaptureTime = now;

                System.Diagnostics.Debug.WriteLine($"[Clipboard] {draft.Type} (Sensitive: {isSensitive})");

                // ── 持久化到 SQLite ──
                // 修改 draft 以包含敏感标记（由于 draft 是 record，我们需要 With 语法或更新构造）
                var finalDraft = draft with { Tag = isSensitive ? Core.Models.ClipboardItemTag.Important : draft.Tag };
                
                var id = await _storage.SaveItemAsync(finalDraft);
                
                // 如果识别为敏感，额外更新数据库标记
                if (isSensitive)
                {
                    await _storage.UpdateSensitiveFlagAsync(id, true);
                }

                System.Diagnostics.Debug.WriteLine($"[Storage] 已保存条目, id={id}");

                // 刷新 UI
                _uiDispatcher?.TryEnqueue(async () =>
                {
                    if (_mainWindow != null)
                    {
                        await _mainWindow.RefreshList();
                        System.Diagnostics.Debug.WriteLine("[UI] 列表刷新请求已发出");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Clipboard] 监听回调处理异常: {ex.Message}");
            }
        }

        private string BuildFingerprint(Core.Models.ClipboardItemDraft draft)
        {
            if (draft.Type == Core.Models.ClipboardItemType.Text)
                return $"TXT:{draft.Text?.GetHashCode()}";
            if (draft.Type == Core.Models.ClipboardItemType.Files)
                return $"FIL:{string.Join("|", draft.FilePaths ?? Array.Empty<string>()).GetHashCode()}";
            if (draft.Type == Core.Models.ClipboardItemType.Image)
                return $"IMG:{draft.ImagePng?.Length}";
            return "UNKNOWN";
        }

        private bool IdentifySensitiveContent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            // 常见的账号密码特征匹配
            string[] patterns = {
                @"(?i)(password|passwd|pwd|密\s*码|口令)[:：= ]+\s*(\S+)",
                @"(?i)(account|user|login|账\s*号|用户名)[:：= ]+\s*(\S+)",
                @"(?i)(secret|token|key|api[_-]?key|密钥)[:：= ]+\s*(\S+)",
                @"(?i)(id[_-]?card|身份证|手机号|tel|phone)[:：= ]+\s*(\d{11,18})",
                @"(?i)(cvv|cvv2|card[_-]?no|银行卡)[:：= ]+\s*(\d{12,19})"
            };

            int matchCount = 0;
            foreach (var p in patterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(text, p))
                {
                    matchCount++;
                }
            }

            // 如果匹配到多个项，或者明确包含密码/密钥类关键字，标记为敏感
            return matchCount >= 2 || 
                   System.Text.RegularExpressions.Regex.IsMatch(text, @"(?i)password|密码|secret|token|apikey|密钥");
        }
    }
}
