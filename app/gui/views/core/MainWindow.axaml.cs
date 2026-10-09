namespace JoinCode.Gui.Views;

/// <summary>
/// 主窗口 code-behind — 仅承载窗口级视图逻辑（窗口尺寸、Activity Bar、主题切换）。
/// 消息区逻辑在 MessageAreaView，停靠布局由 Dock.Avalonia DockControl 管理。
/// 业务均走 ViewModel。
/// </summary>
public sealed partial class MainWindow : Window {
    private MainViewModel? _vm;

    /// <summary>工具调用倒计时刷新计时器 — 每 100ms 更新正在运行的工具的已运行时长</summary>
    private readonly Avalonia.Threading.DispatcherTimer _toolTimer = new() {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    /// <summary>全局状态条心跳计时器 — 500ms 驱动耗时刷新与卡死检测转移</summary>
    private readonly Avalonia.Threading.DispatcherTimer _runStatusTimer = new() {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    /// <summary>GUI 偏好存储 — 窗口尺寸/位置持久化(独立于 ViewModel,启动时直接读取)</summary>
    private readonly Persistence.GuiPreferencesStore _prefsStore = new(new IO.FileSystem.PhysicalFileSystem());

    /// <summary>初始化 MainWindow 实例</summary>
    public MainWindow() {
        App.LogDiag("[MainWindow] ctor begin");
        InitializeComponent();
        App.LogDiag("[MainWindow] ctor end");
        _toolTimer.Tick += OnToolTimerTick;
        _runStatusTimer.Tick += OnRunStatusTimerTick;
        _runStatusTimer.Start();
        Closed += OnWindowClosed;
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        SizeChanged += OnWindowSizeChanged;
        RestoreWindowBounds();
    }

    /// <summary>从 gui-preferences.json 恢复窗口尺寸/位置</summary>
    private void RestoreWindowBounds() {
        try {
            var prefs = _prefsStore.LoadAsync().GetAwaiter().GetResult();
            if (prefs.WindowWidth >= MinWidth)
                Width = prefs.WindowWidth;
            if (prefs.WindowHeight >= MinHeight)
                Height = prefs.WindowHeight;
            if (prefs.WindowX != 0 || prefs.WindowY != 0)
                Position = new Avalonia.PixelPoint(prefs.WindowX, prefs.WindowY);
        } catch (Exception ex) {
            App.LogDiag($"[MainWindow] RestoreWindowBounds failed: {ex.Message}");
        }
    }

    /// <summary>保存窗口尺寸/位置到 gui-preferences.json</summary>
    private void SaveWindowBounds() {
        try {
            var prefs = _prefsStore.LoadAsync().GetAwaiter().GetResult();
            prefs.WindowWidth = Width;
            prefs.WindowHeight = Height;
            prefs.WindowX = Position.X;
            prefs.WindowY = Position.Y;
            _ = _prefsStore.SaveAsync(prefs);
        } catch (Exception ex) {
            App.LogDiag($"[MainWindow] SaveWindowBounds failed: {ex.Message}");
        }
    }

    /// <summary>窗口尺寸变化 — 根据宽度切换紧凑布局(openCode 风格响应式分栏)</summary>
    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) {
        if (_vm is null)
            return;
        _vm.IsCompactLayout = e.NewSize.Width < MainViewModel.CompactLayoutThreshold;
    }

    /// <summary>双击 ESC 判定窗口 — 两次按键间隔上限</summary>
    private const double DoubleEscWindowMs = 600;
    private DateTime _lastEscapeAt = DateTime.MinValue;

    /// <summary>
    /// F2：全局隧道键处理 — 600ms 内双击 ESC 终止当前视图看见的对话框（新增需求）。
    /// 当前聚焦子会话 → 仅终止该 subAgent；当前聚焦主会话 → 终止主会话发送。
    /// 遥测网络为独立服务不受影响。F3 快捷键面板可关闭该手势。
    /// </summary>
    private void OnGlobalKeyDown(object? sender, KeyEventArgs e) {
        if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Control) {
            this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "SessionSearchBox")?.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift && _vm is not null) {
            if (_vm.CyclePermissionModeCommand.CanExecute(null))
                _vm.CyclePermissionModeCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key is not Key.Escape || _vm is null)
            return;
        if (!_vm.DoubleEscStop) // F3 快捷键面板可关闭该手势
            return;

        var now = DateTime.Now;
        if ((now - _lastEscapeAt).TotalMilliseconds <= DoubleEscWindowMs) {
            _lastEscapeAt = DateTime.MinValue; // 消费，防止三连击触发两次终止
            if (_vm.StopGeneratingCommand.CanExecute(null))
                _vm.StopGeneratingCommand.Execute(null);
            if (_vm.ResetCountersCommand.CanExecute(null))
                _vm.ResetCountersCommand.Execute(null);
            e.Handled = true;
            return;
        }
        _lastEscapeAt = now;
    }

    /// <summary>全局按下捕获：补全面板打开时，点击面板外区域收起面板</summary>
    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (_vm is not { IsSlashPopupOpen: true })
            return;
        var slashPalette = this.GetVisualDescendants().OfType<SlashPaletteView>().FirstOrDefault();
        if (slashPalette is null)
            return;
        var hit = this.InputHitTest(e.GetCurrentPoint(this).Position) as Visual;
        if (hit is not null && (ReferenceEquals(hit, slashPalette) || this.GetVisualDescendants().Contains(hit)))
            return;
        _vm.CloseSlashPopup();
    }

    private void OnWindowClosed(object? sender, EventArgs e) {
        SaveWindowBounds();
        _toolTimer.Stop();
        _toolTimer.Tick -= OnToolTimerTick;
        _runStatusTimer.Stop();
        _runStatusTimer.Tick -= OnRunStatusTimerTick;
        RemoveHandler(PointerPressedEvent, OnGlobalPointerPressed);
        if (_vm is not null) {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.ExitRequested -= OnExitRequested;
            _vm.TranscriptRequested -= OnTranscriptRequested;
            _vm.RunStatus.MarqueeStopped -= OnMarqueeStopped;
            _ = _vm.DisposeAsync().AsTask().ContinueWith(t => {
                if (t.IsFaulted) App.LogDiag($"[MainWindow] DisposeAsync failed: {t.Exception?.Message}");
            }, TaskScheduler.Default);
        }
        Closed -= OnWindowClosed;
    }

    /// <summary>数据上下文变更时处理</summary>
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (_vm is not null) {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.ExitRequested -= OnExitRequested;
            _vm.TranscriptRequested -= OnTranscriptRequested;
            _vm.RunStatus.MarqueeStopped -= OnMarqueeStopped;
        }
        _vm = DataContext as MainViewModel;
        if (_vm is not null) {
            _vm.PermissionConfirmCallback = ShowPermissionDialogAsync;
            _vm.AskUserQuestionCallback = ShowAskUserQuestionDialogAsync;
            _vm.SlashConfirmHandler = ShowConfirmDialog;
            _vm.OpenFileCallback = OnOpenFile;
            _vm.ExitRequested += OnExitRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.TranscriptRequested += OnTranscriptRequested;
            _vm.RunStatus.MarqueeStopped += OnMarqueeStopped;
            ApplyAppearance();
            CenterOnScreen();
            _vm.LoadFileTree(System.IO.Directory.GetCurrentDirectory());
        }
    }

    /// <summary>首次绑定和偏好变更时统一应用主题、强调色与动效。</summary>
    private void ApplyAppearance() {
        if (_vm is null) return;
        GuiPalette.CurrentVariant = _vm.CurrentTheme;
        RequestedThemeVariant = _vm.IsDarkTheme
            ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        if (_vm.SelectedAccent is { } accent) GuiAppResources.ApplyAccent(accent);
        GuiAppResources.ApplyTheme(_vm.CurrentTheme);
        Classes.Set("motion", _vm.AnimationsEnabled);
        if (Application.Current is { } app)
            app.Resources["GuiMotionDuration"] = _vm.AnimationsEnabled ? TimeSpan.FromMilliseconds(160) : TimeSpan.Zero;
    }

    /// <summary>窗口居中屏幕 — 在打开时固定到屏幕中间</summary>
    private void CenterOnScreen() {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var workArea = screen.WorkingArea;
        var x = workArea.X + (workArea.Width - Width) / 2;
        var y = workArea.Y + (workArea.Height - Height) / 2;
        Position = new Avalonia.PixelPoint((int)x, (int)y);
    }

    /// <summary>打开子代理回放窗口 — 只读快照，可多开（每 agent 一窗）</summary>
    private void OnTranscriptRequested(BackgroundAgentItemVm run) {
        var window = new TranscriptWindow(run);
        window.Show(this);
    }

    /// <summary>打开文件 — 在内嵌编辑器面板中打开(非弹窗)</summary>
    private void OnOpenFile(string path) {
        _vm?.OpenEditorFile(path);
    }

    /// <summary>T9：斜杠命令确认回调 — 弹极简确认窗；后台线程经 UI 线程同步等待（对齐 TUI painter.Invoke 模式）</summary>
    private bool ShowConfirmDialog(string message) {
        var task = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => {
            var dialog = new ConfirmDialogWindow(message);
            return await dialog.ShowDialog<bool?>(this);
        });
        return task.GetAwaiter().GetResult() == true;
    }

    /// <summary>T9：/exit 确认通过 → 关闭主窗口</summary>
    private void OnExitRequested() => Close();

    /// <summary>需求9：走马灯异常/卡死停止时弹模态提醒，避免用户不知情（Normal/UserAborted 静默）</summary>
    private void OnMarqueeStopped(MarqueeStopReason reason) {
        if (reason is MarqueeStopReason.Normal
            or MarqueeStopReason.UserAborted)
            return;
        var message = reason == MarqueeStopReason.Stalled
            ? "连接似乎已中断（3 秒无响应），对话已停止。\n如需重试请重新发送消息。"
            : "连接异常，对话已停止。\n详情见 dumps/send_error.log。";
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => {
            var dialog = new ConfirmDialogWindow(message);
            await dialog.ShowDialog<bool?>(this);
        });
    }

    /// <summary>权限确认回调：弹出确认框并把用户决策返回给网关；关闭窗口等价于拒绝</summary>
    private async Task<Hosting.PermissionConfirmationDecision> ShowPermissionDialogAsync(
        Hosting.PermissionConfirmationRequest request) {
        var dialog = new PermissionDialog(request);
        return await dialog.ShowDialog<Hosting.PermissionConfirmationDecision>(this);
    }

    /// <summary>AskUserQuestion 回调：弹出多选对话框获取用户选择；关闭窗口等价于取消</summary>
    private async Task<AskUserQuestionResult> ShowAskUserQuestionDialogAsync(QuestionItem question) {
        var dialog = new AskUserQuestionDialog(question);
        return (await dialog.ShowDialog<AskUserQuestionResult>(this)) ?? AskUserQuestionResult.CancelledResult();
    }

    /// <summary>窗口级快捷键：Ctrl+N 新建会话 / Ctrl+L 清空 / Esc 收起设置面板或停止生成</summary>
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (_vm is null)
            return;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (ctrl && e.Key == Key.N) {
            e.Handled = true;
            _vm.NewConversationCommand.Execute(null);
        } else if (ctrl && e.Key == Key.L) {
            e.Handled = true;
            _vm.ClearHistoryCommand.Execute(null);
        } else if (e.Key == Key.Escape) {
            if (_vm.IsZenMode) {
                e.Handled = true;
                _vm.ToggleZenModeCommand.Execute(null);
            } else if (_vm.IsSettingsPanelActive) {
                e.Handled = true;
                _vm.ToggleSidePanelCommand.Execute(SidePanelKind.Settings);
            } else if (_vm.CanStop) {
                e.Handled = true;
                _vm.StopGeneratingCommand.Execute(null);
            }
        }
    }

    /// <summary>ViewModel 状态变化时联动 View（主题切换、Activity Bar 等窗口级响应）</summary>
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(MainViewModel.AccentId) or nameof(MainViewModel.AnimationsEnabled)) {
            ApplyAppearance();
        } else if (e.PropertyName == nameof(MainViewModel.CurrentTheme)) {
            GuiPalette.CurrentVariant = _vm!.CurrentTheme;
            var isLight = _vm.CurrentTheme is GuiPalette.GuiThemeVariant.Light or GuiPalette.GuiThemeVariant.SolarizedLight;
            RequestedThemeVariant = isLight
                ? Avalonia.Styling.ThemeVariant.Light
                : Avalonia.Styling.ThemeVariant.Dark;
            GuiAppResources.ApplyTheme(_vm.CurrentTheme);
        } else if (e.PropertyName == nameof(MainViewModel.IsBusy)) {
            if (_vm!.IsBusy)
                _toolTimer.Start();
            else
                _toolTimer.Stop();
        }
    }

    /// <summary>工具倒计时 tick：刷新所有正在运行工具消息的已运行时长</summary>
    private void OnToolTimerTick(object? sender, EventArgs e) {
        if (_vm is null)
            return;
        foreach (var m in _vm.Messages) {
            if (m.IsToolRunning)
                m.RefreshElapsed();
        }
    }

    /// <summary>全局状态条心跳 tick：耗时刷新 + 卡死检测状态转移；面板打开期间同步后台代理快照</summary>
    private void OnRunStatusTimerTick(object? sender, EventArgs e) {
        if (_vm is null)
            return;
        _vm.RunStatus.OnHeartbeatTick();
        if (_vm.BackgroundPanel.IsOpen)
            _ = _vm.BackgroundPanel.RefreshAsync();
    }
}
