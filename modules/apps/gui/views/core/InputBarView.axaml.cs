namespace JoinCode.Gui.Views;

/// <summary>
/// 底部输入栏 UserControl — composer 卡片（透明无边框 TextBox 内嵌 + 发送按钮嵌入卡片右下）
/// + 字符计数 + 停止/发送按钮 + 分隔线/时间戳快捷按钮。
/// 斜杠补全面板在 <see cref="SlashPaletteView"/>（MainWindow 布局行，位于本组件正上方同列）；
/// 本组件负责键盘事件（Enter 发送/Up-Down 历史导航/补全导航）与 30ms 输入防抖。
/// </summary>
public sealed partial class InputBarView : UserControl {
    /// <summary>斜杠命令补全防抖计时器 — 30ms 内多次输入/光标变化合并为一次刷新</summary>
    private readonly DispatcherTimer _slashDebounceTimer = new() {
        Interval = TimeSpan.FromMilliseconds(30)
    };

    private MainViewModel? _vm;

    /// <summary>初始化 InputBarView 实例</summary>
    public InputBarView() {
        InitializeComponent();
        _slashDebounceTimer.Tick += OnSlashDebounceTick;
        if (InputTextBox is not null)
            InputTextBox.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        InputBarRoot.PointerMoved += OnInputResizePointerMoved;
        InputBarRoot.PointerReleased += OnInputResizePointerReleased;
    }

    /// <summary>当前 MainViewModel（供外部访问）</summary>
    public MainViewModel? ViewModel => _vm;

    /// <summary>数据上下文变更时处理</summary>
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (_vm is not null) {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.FocusInputRequested -= FocusInput;
        }
        _vm = DataContext as MainViewModel;
        if (_vm is not null) {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.FocusInputRequested += FocusInput;
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.InputText))
            StartSlashDebounce();
    }

    /// <summary>启动斜杠补全防抖（30ms 后合并刷新）</summary>
    private void StartSlashDebounce() {
        _slashDebounceTimer.Stop();
        _slashDebounceTimer.Start();
    }

    /// <summary>防抖到期：同步光标位置并刷新斜杠建议</summary>
    private void OnSlashDebounceTick(object? sender, EventArgs e) {
        _slashDebounceTimer.Stop();
        if (_vm is null || InputTextBox is null)
            return;
        _vm.InputCaretIndex = InputTextBox.CaretIndex;
        _vm.RefreshSlashSuggestions();
    }

    /// <summary>聚焦输入框并把光标移到末尾（命令补全后由宿主调用）</summary>
    public void FocusInput() {
        if (InputTextBox is null)
            return;
        InputTextBox.Focus();
        InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e) {
        if (DataContext is not MainViewModel vm)
            return;

        // 用户打字重置子代理空闲倒计时（任何按键都算活动，别移交 mainAgent）
        vm.ResetIdleTimer();

        if (vm.IsSlashPopupOpen) {
            if (e.Key == Key.Down) {
                e.Handled = true;
                vm.SlashNavigate(1);
                return;
            }
            if (e.Key == Key.Up) {
                e.Handled = true;
                vm.SlashNavigate(-1);
                return;
            }
            if (e.Key == Key.Enter || e.Key == Key.Tab) {
                e.Handled = true;
                vm.CompleteSlashSuggestion();
                FocusInput();
                return;
            }
            if (e.Key == Key.Escape) {
                e.Handled = true;
                vm.CloseSlashPopup();
                return;
            }
        }

        if (e.Key == Key.Enter) {
            // F3 快捷键面板驱动：EnterSends=true → Enter 发送；false → Ctrl+Enter 发送、Enter 换行
            var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            var sendPressed = vm.EnterSends ? !shift : ctrl;

            if (sendPressed)
                HandleEnterSend(e, vm);
            else
                HandleEnterNewLine(e, vm, sender);
        } else if (e.Key == Key.Up && !vm.IsSlashPopupOpen) {
            e.Handled = true;
            vm.NavigateHistoryCommand.Execute(-1);
        } else if (e.Key == Key.Down && !vm.IsSlashPopupOpen) {
            e.Handled = true;
            vm.NavigateHistoryCommand.Execute(1);
        } else if (e.Key == Key.Left || e.Key == Key.Right) {
            StartSlashDebounce();
        }
    }

    private static void HandleEnterSend(KeyEventArgs e, MainViewModel vm) {
        if (vm.IsBusy)
            return;
        e.Handled = true;
        // 用户发送消息取消空闲倒计时 — 主动接管，立即恢复子代理
        vm.StopIdleTimer();
        vm.SendCommand.Execute(null);
    }

    private static void HandleEnterNewLine(KeyEventArgs e, MainViewModel vm, object? sender) {
        e.Handled = true;
        if (sender is TextBox textBox) {
            var caret = textBox.CaretIndex;
            vm.InputText = (textBox.Text ?? string.Empty).Insert(caret, "\n");
            textBox.CaretIndex = caret + 1;
        }
    }

    /// <summary>模型选择器 Popup 鼠标离开时自动关闭</summary>
    private void OnModelPickerPointerExited(object? sender, Avalonia.Input.PointerEventArgs e) {
        if (DataContext is MainViewModel vm)
            vm.IsModelPickerOpen = false;
    }

    /// <summary>发送方式 Popup 鼠标离开时自动关闭</summary>
    private void OnSendModePopupPointerExited(object? sender, Avalonia.Input.PointerEventArgs e) {
        if (DataContext is not MainViewModel vm)
            return;
        vm.IsSendModePopupOpen = false;
    }

    private bool _isResizingInput;
    private double _resizeStartY;
    private double _resizeStartHeight;

    /// <summary>拖拽手柄按下 — 记录起始 Y 坐标和输入框高度,capture 到根控件确保移出手柄仍收事件</summary>
    private void OnInputResizePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
        if (DataContext is not MainViewModel vm)
            return;
        _isResizingInput = true;
        _resizeStartY = e.GetPosition(null).Y;
        _resizeStartHeight = vm.InputAreaHeight;
        e.Pointer.Capture(InputBarRoot);
        e.Handled = true;
    }

    /// <summary>拖拽手柄移动 — 向上拖增大高度,向下拖减小高度</summary>
    private void OnInputResizePointerMoved(object? sender, Avalonia.Input.PointerEventArgs e) {
        if (!_isResizingInput || DataContext is not MainViewModel vm)
            return;
        var currentY = e.GetPosition(null).Y;
        var delta = _resizeStartY - currentY;
        var newHeight = _resizeStartHeight + delta;
        if (newHeight < 38)
            newHeight = 38;
        if (newHeight > 240)
            newHeight = 240;
        vm.InputAreaHeight = newHeight;
    }

    /// <summary>拖拽手柄释放 — 结束拖拽,释放 capture</summary>
    private void OnInputResizePointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e) {
        if (!_isResizingInput)
            return;
        _isResizingInput = false;
        e.Pointer.Capture(null);
    }

    /// <summary>从视觉树分离时处理</summary>
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
        _slashDebounceTimer.Stop();
        _slashDebounceTimer.Tick -= OnSlashDebounceTick;
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        if (InputTextBox is not null)
            InputTextBox.RemoveHandler(InputElement.KeyDownEvent, OnInputKeyDown);
        InputBarRoot.PointerMoved -= OnInputResizePointerMoved;
        InputBarRoot.PointerReleased -= OnInputResizePointerReleased;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Ctrl+滚轮=放大缩小日志字体,普通滚轮=滚动</summary>
    private void OnLogPointerWheel(object? sender, Avalonia.Input.PointerWheelEventArgs e) {
        if (DataContext is not MainViewModel vm)
            return;
        if ((e.KeyModifiers & KeyModifiers.Control) != 0) {
            e.Handled = true;
            if (e.Delta.Y > 0)
                vm.EnlargeLogFontCommand.Execute(null);
            else if (e.Delta.Y < 0)
                vm.ShrinkLogFontCommand.Execute(null);
        }
    }

    /// <summary>鼠标中键双击=重置日志字体到默认10</summary>
    private void OnLogPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
        var point = e.GetCurrentPoint(null);
        if (point.Properties.IsMiddleButtonPressed && e.ClickCount >= 2) {
            e.Handled = true;
            if (DataContext is MainViewModel vm)
                vm.StatusLogFontSize = 10;
        }
    }

    /// <summary>保存会话为 Markdown — 通过 StorageProvider 弹出保存对话框</summary>
    private void OnSaveMarkdown(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _ = SaveMarkdownAsync();

    private async Task SaveMarkdownAsync() {
        if (DataContext is not MainViewModel vm || !vm.HasMessages) return;
        var snapshot = vm.ExportSessionMarkdown;
        try {
            var storage = Avalonia.Controls.TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;
            using var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions {
                Title = "导出会话为 Markdown",
                SuggestedFileName = $"JoinCode-{DateTime.Now:yyyyMMdd-HHmmss}.md",
                DefaultExtension = "md",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("Markdown") { Patterns = ["*.md"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(snapshot);
            vm.StatusText = "Markdown 已导出";
        } catch (Exception ex) {
            vm.StatusText = $"导出失败: {ex.Message}";
        }
    }
}