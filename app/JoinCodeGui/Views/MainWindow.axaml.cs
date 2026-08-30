namespace JoinCode.Gui.Views;

/// <summary>
/// 主窗口 code-behind — 仅承载视图逻辑（输入回车发送、新消息自动滚动到底、错误 toast 显示）。
/// 消息区为 ItemsControl 模板化渲染（G3）：Markdown 正文 + 单条复制/删除/思考折叠命令。
/// 业务均走 ViewModel。
/// </summary>
public sealed partial class MainWindow : Window
{
    private MainViewModel? _vm;

    private System.Threading.CancellationTokenSource? _toastCts;

    private System.Threading.CancellationTokenSource? _errorToastFadeCts;

    private bool _autoScrollEnabled = true;

    private static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(5);

    private readonly Avalonia.Threading.DispatcherTimer _errorToastTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    private int _errorToastRemainingMs;

    /// <summary>工具调用倒计时刷新计时器 — 每 100ms 更新正在运行的工具的已运行时长</summary>
    private readonly Avalonia.Threading.DispatcherTimer _toolTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    /// <summary>全局状态条心跳计时器 — 500ms 驱动耗时刷新与卡死检测转移</summary>
    private readonly Avalonia.Threading.DispatcherTimer _runStatusTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    public MainWindow()
    {
        App.LogDiag("[MainWindow] ctor begin");
        InitializeComponent();
        App.LogDiag("[MainWindow] ctor end");
        _errorToastTimer.Tick += OnErrorToastTimerTick;
        _toolTimer.Tick += OnToolTimerTick;
        _runStatusTimer.Tick += OnRunStatusTimerTick;
        _runStatusTimer.Start();
        Closed += OnWindowClosed;
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>双击 ESC 判定窗口 — 两次按键间隔上限</summary>
    private const double DoubleEscWindowMs = 600;
    private DateTime _lastEscapeAt = DateTime.MinValue;

    /// <summary>
    /// F2：全局隧道键处理 — 600ms 内双击 ESC 终止当前视图看见的对话框（新增需求）。
    /// 当前聚焦子会话 → 仅终止该 subAgent；当前聚焦主会话 → 终止主会话发送。
    /// 遥测网络为独立服务不受影响。F3 快捷键面板可关闭该手势。
    /// </summary>
    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape || _vm is null)
            return;
        if (!_vm.DoubleEscStop) // F3 快捷键面板可关闭该手势
            return;

        var now = DateTime.Now;
        if ((now - _lastEscapeAt).TotalMilliseconds <= DoubleEscWindowMs)
        {
            _lastEscapeAt = DateTime.MinValue; // 消费，防止三连击触发两次终止
            if (_vm.StopGeneratingCommand.CanExecute(null))
                _vm.StopGeneratingCommand.Execute(null);
            e.Handled = true;
            return;
        }
        _lastEscapeAt = now;
    }

    /// <summary>点击候选项完成补全 → 回焦输入框</summary>
    private void OnSlashPaletteCompleted(object? sender, RoutedEventArgs e) => InputBar?.FocusInput();

    /// <summary>全局按下捕获：补全面板打开时，点击面板外区域收起面板</summary>
    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is not { IsSlashPopupOpen: true } || SlashPalette is null)
            return;
        var hit = this.InputHitTest(e.GetCurrentPoint(this).Position) as Visual;
        if (hit is not null && (ReferenceEquals(hit, SlashPalette) || this.GetVisualDescendants().Contains(hit)))
            return;
        _vm.CloseSlashPopup();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _errorToastTimer.Stop();
        _errorToastTimer.Tick -= OnErrorToastTimerTick;
        _toolTimer.Stop();
        _toolTimer.Tick -= OnToolTimerTick;
        RemoveHandler(PointerPressedEvent, OnGlobalPointerPressed);
        _toastCts?.Cancel();
        _errorToastFadeCts?.Cancel();
        if (_vm is not null)
            _vm.ScrollToBottomRequested -= OnScrollToBottomRequested;
        Closed -= OnWindowClosed;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.Messages.CollectionChanged -= OnMessagesChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.ScrollToBottomRequested -= OnScrollToBottomRequested;
            _vm.ExitRequested -= OnExitRequested;
            _vm.TranscriptRequested -= OnTranscriptRequested;
            _vm.RunStatus.MarqueeStopped -= OnMarqueeStopped;
        }
        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PermissionConfirmCallback = ShowPermissionDialogAsync;
            _vm.AskUserQuestionCallback = ShowAskUserQuestionDialogAsync;
            _vm.SlashConfirmHandler = ShowConfirmDialog;
            _vm.ExitRequested += OnExitRequested;
            _vm.Messages.CollectionChanged += OnMessagesChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.ScrollToBottomRequested += OnScrollToBottomRequested;
            _vm.TranscriptRequested += OnTranscriptRequested;
            _vm.RunStatus.MarqueeStopped += OnMarqueeStopped;
        }
    }

    /// <summary>打开子代理回放窗口 — 只读快照，可多开（每 agent 一窗）</summary>
    private void OnTranscriptRequested(SubAgentRun run)
    {
        var window = new TranscriptWindow(run);
        window.Show(this);
    }

    /// <summary>T9：斜杠命令确认回调 — 弹极简确认窗；后台线程经 UI 线程同步等待（对齐 TUI painter.Invoke 模式）</summary>
    private bool ShowConfirmDialog(string message)
    {
        var task = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ConfirmDialogWindow(message);
            return await dialog.ShowDialog<bool?>(this);
        });
        return task.GetAwaiter().GetResult() == true;
    }

    /// <summary>T9：/exit 确认通过 → 关闭主窗口</summary>
    private void OnExitRequested() => Close();

    /// <summary>需求9：走马灯异常/卡死停止时弹模态提醒，避免用户不知情（Normal/UserAborted 静默）</summary>
    private void OnMarqueeStopped(MarqueeStopReason reason)
    {
        if (reason is MarqueeStopReason.Normal
            or MarqueeStopReason.UserAborted)
            return;
        var message = reason == MarqueeStopReason.Stalled
            ? "连接似乎已中断（3 秒无响应），对话已停止。\n如需重试请重新发送消息。"
            : "连接异常，对话已停止。\n详情见 dumps/send_error.log。";
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ConfirmDialogWindow(message);
            await dialog.ShowDialog<bool?>(this);
        });
    }

    /// <summary>权限确认回调：弹出确认框并把用户决策返回给网关；关闭窗口等价于拒绝</summary>
    private async Task<Hosting.PermissionConfirmationDecision> ShowPermissionDialogAsync(
        Hosting.PermissionConfirmationRequest request)
    {
        var dialog = new PermissionDialog(request);
        return await dialog.ShowDialog<Hosting.PermissionConfirmationDecision>(this);
    }

    /// <summary>AskUserQuestion 回调：弹出多选对话框获取用户选择；关闭窗口等价于取消</summary>
    private async Task<AskUserQuestionResult> ShowAskUserQuestionDialogAsync(QuestionItem question)
    {
        var dialog = new AskUserQuestionDialog(question);
        return await dialog.ShowDialog<AskUserQuestionResult>(this) ?? AskUserQuestionResult.CancelledResult();
    }

    /// <summary>窗口级快捷键：Ctrl+N 新建会话 / Ctrl+L 清空 / Esc 收起设置面板或停止生成</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null)
            return;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (ctrl && e.Key == Key.N)
        {
            e.Handled = true;
            _vm.NewConversationCommand.Execute(null);
        }
        else if (ctrl && e.Key == Key.L)
        {
            e.Handled = true;
            _vm.ClearHistoryCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            if (_vm.IsSettingsPanelOpen)
            {
                e.Handled = true;
                _vm.ToggleSettingsPanelCommand.Execute(null);
            }
            else if (_vm.CanStop)
            {
                e.Handled = true;
                _vm.StopGeneratingCommand.Execute(null);
            }
        }
    }

    /// <summary>ViewModel 状态变化时联动 View（主题切换、复制反馈 toast 等视图级响应）</summary>
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDarkTheme))
        {
            GuiPalette.CurrentVariant = _vm!.IsDarkTheme
                ? GuiPalette.GuiThemeVariant.Dark
                : GuiPalette.GuiThemeVariant.Light;
            RequestedThemeVariant = _vm.IsDarkTheme
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
            // 重新赋值 DataContext，强制所有转换器按新主题重算颜色（气泡/指示器/角色标签）
            var dc = DataContext;
            DataContext = null;
            DataContext = dc;
        }
        else if (e.PropertyName == nameof(MainViewModel.HasCopied) && _vm!.HasCopied)
        {
            ScheduleCopyToastHide();
        }
        else if (e.PropertyName == nameof(MainViewModel.CopiedMessageCopy) && !string.IsNullOrEmpty(_vm!.CopiedMessageCopy))
        {
            Clipboard?.SetTextAsync(_vm.CopiedMessageCopy);
            _vm.ClearCopiedMessageCopy();
            ScheduleCopyToastHide();
        }
        else if (e.PropertyName == nameof(MainViewModel.ExportedSessionCopy) && !string.IsNullOrEmpty(_vm!.ExportedSessionCopy))
        {
            Clipboard?.SetTextAsync(_vm.ExportedSessionCopy);
            _vm.ClearSessionExport();
            ScheduleCopyToastHide();
        }
        else if (e.PropertyName == nameof(MainViewModel.ErrorToastText))
        {
            if (_vm!.HasErrorToast)
                ShowErrorToast();
            else
                HideErrorToast();
        }
        else if (e.PropertyName == nameof(MainViewModel.ErrorToastCopy) && !string.IsNullOrEmpty(_vm!.ErrorToastCopy))
        {
            Clipboard?.SetTextAsync(_vm.ErrorToastCopy);
            _vm.ClearErrorToastCopy();
            ScheduleCopyToastHide();
        }
        else if (e.PropertyName == nameof(MainViewModel.StatusText))
        {
        }
        else if (e.PropertyName == nameof(MainViewModel.IsBusy))
        {
            if (_vm!.IsBusy)
                _toolTimer.Start();
            else
                _toolTimer.Stop();
        }
        // G3：消息区改为 ItemsControl 模板化渲染，FilteredMessages 绑定自动更新，
        // AllMessagesText 仅保留导出/复制用途，不再驱动显示
    }

    /// <summary>工具倒计时 tick：刷新所有正在运行工具消息的已运行时长</summary>
    private void OnToolTimerTick(object? sender, EventArgs e)
    {
        if (_vm is null)
            return;
        foreach (var m in _vm.Messages)
        {
            if (m.IsToolRunning)
                m.RefreshElapsed();
        }
    }

    /// <summary>全局状态条心跳 tick：耗时刷新 + 卡死检测状态转移；面板打开期间同步后台代理快照</summary>
    private void OnRunStatusTimerTick(object? sender, EventArgs e)
    {
        if (_vm is null)
            return;
        _vm.RunStatus.OnHeartbeatTick();
        if (_vm.BackgroundPanel.IsOpen)
            _ = _vm.BackgroundPanel.RefreshAsync();
    }

    /// <summary>1.5s 后自动隐藏"已复制" toast（每次复制重置计时）</summary>
    private void ScheduleCopyToastHide()
    {
        _toastCts?.Cancel();
        _toastCts = new System.Threading.CancellationTokenSource();
        var token = _toastCts.Token;
        _ = Task.Delay(1500, token).ContinueWith(
            _ => _vm?.ClearCopiedState(),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>显示错误 toast：淡入并启动 5s 自动隐藏计时（hover 暂停）</summary>
    private void ShowErrorToast()
    {
        _errorToastFadeCts?.Cancel();
        ErrorToast.Opacity = 1;
        _errorToastRemainingMs = (int)ErrorToastDuration.TotalMilliseconds;
        _errorToastTimer.Start();
    }

    /// <summary>隐藏错误 toast：立即停止计时并清除状态（✕/复制按钮走此路径）</summary>
    private void HideErrorToast()
    {
        _errorToastTimer.Stop();
        _errorToastFadeCts?.Cancel();
    }

    /// <summary>错误 toast 计时 tick：倒计时结束则淡出（0.45s 过渡后清除状态）</summary>
    private void OnErrorToastTimerTick(object? sender, EventArgs e)
    {
        _errorToastRemainingMs -= (int)_errorToastTimer.Interval.TotalMilliseconds;
        if (_errorToastRemainingMs <= 0)
        {
            _errorToastTimer.Stop();
            StartErrorToastFadeOut();
        }
    }

    /// <summary>淡出错误 toast：透明度动画结束后清除 VM 状态（触发 IsVisible=false）</summary>
    private void StartErrorToastFadeOut()
    {
        _errorToastFadeCts?.Cancel();
        _errorToastFadeCts = new System.Threading.CancellationTokenSource();
        var token = _errorToastFadeCts.Token;
        ErrorToast.Opacity = 0;
        _ = Task.Delay(500, token).ContinueWith(
            _ => _vm?.DismissErrorToastCommand.Execute(null),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>鼠标悬停在 toast 上：暂停自动隐藏计时并取消淡出（维持显示）</summary>
    private void OnErrorToastPointerEnter(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        _errorToastTimer.Stop();
        _errorToastFadeCts?.Cancel();
        ErrorToast.Opacity = 1;
    }

    /// <summary>鼠标离开 toast：恢复自动隐藏计时（已到期的立即淡出）</summary>
    private void OnErrorToastPointerLeave(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (_vm is { HasErrorToast: true })
        {
            if (_errorToastRemainingMs <= 0)
                StartErrorToastFadeOut();
            else
                _errorToastTimer.Start();
        }
    }

    /// <summary>新消息加入时，若未上滑浏览则自动滚动到底部（G3：ScrollViewer 化）</summary>
    private void OnMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
            && _autoScrollEnabled
            && MessageScrollViewer is not null)
        {
            MessageScrollViewer.ScrollToEnd();
        }
    }

    /// <summary>滚动变化时：上滑超过阈值则暂停自动滚动并显示回底浮钮</summary>
    private void OnMessageScrollChanged(object? sender, Avalonia.Controls.ScrollChangedEventArgs e)
    {
        var scroll = sender as ScrollViewer;
        if (scroll is null)
            return;
        var isNearBottom = scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 40;
        _autoScrollEnabled = isNearBottom;
        if (_vm is not null)
            _vm.IsBackToBottomVisible = !isNearBottom;
    }

    /// <summary>VM 请求滚动到底部时执行 UI 滚动操作</summary>
    private void OnScrollToBottomRequested()
    {
        MessageScrollViewer?.ScrollToEnd();
        _autoScrollEnabled = true;
    }
}

