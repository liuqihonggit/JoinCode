namespace JoinCode.Gui.Views;

/// <summary>
/// 消息区 View — 消息列表+编辑器+面板+斜杠补全+输入栏+错误 toast。
/// 从 MainWindow 提取，DataContext 为 MainViewModel。
/// </summary>
public sealed partial class MessageAreaView : UserControl {
    private MainViewModel? _vm;

    private bool _autoScrollEnabled = true;

    private System.Threading.CancellationTokenSource? _toastCts;

    private System.Threading.CancellationTokenSource? _errorToastFadeCts;

    private static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(5);

    private readonly Avalonia.Threading.DispatcherTimer _errorToastTimer = new() {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    private int _errorToastRemainingMs;

    /// <summary>面板拖拽调整大小 — 起始 Y 坐标</summary>
    private double _panelResizeStartY;

    /// <summary>面板拖拽调整大小 — 起始 X 坐标</summary>
    private double _panelResizeStartX;

    /// <summary>面板拖拽调整大小 — 起始高度</summary>
    private double _panelResizeStartHeight;

    /// <summary>面板拖拽调整大小 — 起始宽度</summary>
    private double _panelResizeStartWidth;

    /// <summary>面板拖拽中标志</summary>
    private bool _isPanelResizing;

    /// <summary>初始化 MessageAreaView 实例</summary>
    public MessageAreaView() {
        InitializeComponent();
        _errorToastTimer.Tick += OnErrorToastTimerTick;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>斜杠补全面板 — 供 MainWindow 全局点击检测</summary>
    public SlashPaletteView? SlashPaletteControl => SlashPalette;

    /// <summary>输入栏 — 供 MainWindow 焦点控制</summary>
    public InputBarView? InputBarControl => InputBar;

    /// <summary>滚动到底部 — 供外部调用</summary>
    public void ScrollToBottom() {
        MessageScrollViewer?.ScrollToEnd();
        _autoScrollEnabled = true;
    }

    /// <summary>数据上下文变更 — 订阅/取消订阅 VM 事件</summary>
    private void OnDataContextChanged(object? sender, EventArgs e) {
        if (_vm is not null) {
            _vm.Messages.CollectionChanged -= OnMessagesChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.ScrollToBottomRequested -= OnScrollToBottomRequested;
        }
        _vm = DataContext as MainViewModel;
        if (_vm is not null) {
            _vm.Messages.CollectionChanged += OnMessagesChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.ScrollToBottomRequested += OnScrollToBottomRequested;
        }
    }

    /// <summary>点击候选项完成补全 → 回焦输入框</summary>
    private void OnSlashPaletteCompleted(object? sender, RoutedEventArgs e) => InputBar?.FocusInput();

    /// <summary>面板拖拽手柄按下 — 记录起始位置</summary>
    private void OnPanelResizePointerPressed(object? sender, PointerPressedEventArgs e) {
        if (_vm is null)
            return;
        _panelResizeStartX = e.GetCurrentPoint(this).Position.X;
        _panelResizeStartY = e.GetCurrentPoint(this).Position.Y;
        _panelResizeStartHeight = _vm.PanelHeight;
        _panelResizeStartWidth = _vm.PanelWidth;
        _isPanelResizing = true;
        e.Pointer.Capture(sender as Avalonia.Input.IInputElement);
        e.Handled = true;
    }

    /// <summary>面板拖拽手柄移动 — 实时更新面板大小</summary>
    private void OnPanelResizePointerMoved(object? sender, PointerEventArgs e) {
        if (!_isPanelResizing || _vm is null)
            return;
        var dx = e.GetCurrentPoint(this).Position.X - _panelResizeStartX;
        var dy = e.GetCurrentPoint(this).Position.Y - _panelResizeStartY;
        if (_vm.IsPanelTop)
            _vm.PanelHeight = Math.Clamp(_panelResizeStartHeight - dy, 0, 800);
        else if (_vm.IsPanelBottom)
            _vm.PanelHeight = Math.Clamp(_panelResizeStartHeight + dy, 0, 800);
        else if (_vm.IsPanelLeft)
            _vm.PanelWidth = Math.Clamp(_panelResizeStartWidth - dx, 0, 800);
        else if (_vm.IsPanelRight)
            _vm.PanelWidth = Math.Clamp(_panelResizeStartWidth + dx, 0, 800);
        e.Handled = true;
    }

    /// <summary>面板拖拽手柄释放 — 结束拖拽</summary>
    private void OnPanelResizePointerReleased(object? sender, PointerReleasedEventArgs e) {
        if (!_isPanelResizing)
            return;
        _isPanelResizing = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>面板拖拽经过 — 允许 Move 效果</summary>
    private void OnPanelDragOver(object? sender, DragEventArgs e) {
        if (e.Data.Contains("PanelDrag"))
            e.DragEffects = DragDropEffects.Move;
        else
            e.DragEffects = DragDropEffects.None;
    }

    /// <summary>拖拽释放到主区 — 根据鼠标 X 坐标决定停靠左/右</summary>
    private void OnPanelDropMainArea(object? sender, DragEventArgs e) {
        if (_vm is not null && e.Data.Contains("PanelDrag")) {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is not null) {
                var pos = e.GetPosition(topLevel);
                _vm.ActivePanelDock = pos.X < topLevel.Bounds.Width / 2 ? DockPosition.Left : DockPosition.Right;
            }
        }
        e.Handled = true;
    }

    /// <summary>ViewModel 状态变化时联动 View（错误 toast、复制反馈、剪贴板）</summary>
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.HasCopied) && _vm!.HasCopied) {
            ScheduleCopyToastHide();
        } else if (e.PropertyName == nameof(MainViewModel.CopiedMessageCopy) && !string.IsNullOrEmpty(_vm!.CopiedMessageCopy)) {
            SetClipboardText(_vm.CopiedMessageCopy);
            _vm.ClearCopiedMessageCopy();
            ScheduleCopyToastHide();
        } else if (e.PropertyName == nameof(MainViewModel.ExportedSessionCopy) && !string.IsNullOrEmpty(_vm!.ExportedSessionCopy)) {
            SetClipboardText(_vm.ExportedSessionCopy);
            _vm.ClearSessionExport();
            ScheduleCopyToastHide();
        } else if (e.PropertyName == nameof(MainViewModel.ErrorToastText)) {
            if (_vm!.HasErrorToast)
                ShowErrorToast();
            else
                HideErrorToast();
        } else if (e.PropertyName == nameof(MainViewModel.ErrorToastCopy) && !string.IsNullOrEmpty(_vm!.ErrorToastCopy)) {
            SetClipboardText(_vm.ErrorToastCopy);
            _vm.ClearErrorToastCopy();
            ScheduleCopyToastHide();
        }
    }

    /// <summary>设置剪贴板文本 — 通过 TopLevel 获取 Clipboard</summary>
    private void SetClipboardText(string text) {
        var topLevel = TopLevel.GetTopLevel(this);
        topLevel?.Clipboard?.SetTextAsync(text);
    }

    /// <summary>1.5s 后自动隐藏"已复制" toast</summary>
    private void ScheduleCopyToastHide() {
        _toastCts?.Cancel();
        _toastCts = new System.Threading.CancellationTokenSource();
        var token = _toastCts.Token;
        _ = Task.Delay(1500, token).ContinueWith(
            _ => _vm?.ClearCopiedState(),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>显示错误 toast：淡入并启动 5s 自动隐藏计时</summary>
    private void ShowErrorToast() {
        _errorToastFadeCts?.Cancel();
        ErrorToast.Opacity = 1;
        _errorToastRemainingMs = (int)ErrorToastDuration.TotalMilliseconds;
        _errorToastTimer.Start();
    }

    /// <summary>隐藏错误 toast：立即停止计时并清除状态</summary>
    private void HideErrorToast() {
        _errorToastTimer.Stop();
        _errorToastFadeCts?.Cancel();
    }

    /// <summary>错误 toast 计时 tick：倒计时结束则淡出</summary>
    private void OnErrorToastTimerTick(object? sender, EventArgs e) {
        _errorToastRemainingMs -= (int)_errorToastTimer.Interval.TotalMilliseconds;
        if (_errorToastRemainingMs <= 0) {
            _errorToastTimer.Stop();
            StartErrorToastFadeOut();
        }
    }

    /// <summary>淡出错误 toast：透明度动画结束后清除 VM 状态</summary>
    private void StartErrorToastFadeOut() {
        _errorToastFadeCts?.Cancel();
        if (_vm is { AnimationsEnabled: false }) {
            _vm.DismissErrorToastCommand.Execute(null);
            return;
        }
        _errorToastFadeCts = new System.Threading.CancellationTokenSource();
        var token = _errorToastFadeCts.Token;
        ErrorToast.Opacity = 0;
        _ = Task.Delay(500, token).ContinueWith(
            _ => _vm?.DismissErrorToastCommand.Execute(null),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>鼠标悬停在 toast 上：暂停自动隐藏计时</summary>
    private void OnErrorToastPointerEnter(object? sender, Avalonia.Input.PointerEventArgs e) {
        _errorToastTimer.Stop();
        _errorToastFadeCts?.Cancel();
        ErrorToast.Opacity = 1;
    }

    /// <summary>鼠标离开 toast：恢复自动隐藏计时</summary>
    private void OnErrorToastPointerLeave(object? sender, Avalonia.Input.PointerEventArgs e) {
        if (_vm is { HasErrorToast: true }) {
            if (_errorToastRemainingMs <= 0)
                StartErrorToastFadeOut();
            else
                _errorToastTimer.Start();
        }
    }

    /// <summary>新消息加入时，若未上滑浏览则自动滚动到底部</summary>
    private void OnMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
            && _autoScrollEnabled
            && MessageScrollViewer is not null) {
            MessageScrollViewer.ScrollToEnd();
        }
    }

    /// <summary>滚动变化时：上滑超过阈值则暂停自动滚动并显示回底浮钮</summary>
    private void OnMessageScrollChanged(object? sender, Avalonia.Controls.ScrollChangedEventArgs e) {
        var scroll = sender as ScrollViewer;
        if (scroll is null)
            return;
        var isNearBottom = scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 40;
        _autoScrollEnabled = isNearBottom;
        if (_vm is not null)
            _vm.IsBackToBottomVisible = !isNearBottom;
    }

    /// <summary>VM 请求滚动到底部时执行 UI 滚动操作</summary>
    private void OnScrollToBottomRequested() {
        MessageScrollViewer?.ScrollToEnd();
        _autoScrollEnabled = true;
    }
}
