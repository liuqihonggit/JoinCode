using Avalonia.Controls;
using Avalonia.Input;

using JoinCode.Gui.Theming;
using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Views;

/// <summary>
/// 主窗口 code-behind — 仅承载视图逻辑（输入回车发送、新消息自动滚动到底、错误 toast 显示）。
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

    /// <summary>状态点闪烁计时器 — Busy 态每 500ms 切换透明度，提示用户引擎仍在工作</summary>
    private readonly Avalonia.Threading.DispatcherTimer _statusBlinkTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    /// <summary>闪烁亮暗切换标志（true=亮，false=暗）</summary>
    private bool _statusBlinkBright = true;

    /// <summary>工具调用倒计时刷新计时器 — 每 100ms 更新正在运行的工具的已运行时长</summary>
    private readonly Avalonia.Threading.DispatcherTimer _toolTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    /// <summary>斜杠命令补全防抖计时器 — 30ms 内多次输入/光标变化合并为一次刷新</summary>
    private readonly Avalonia.Threading.DispatcherTimer _slashDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(30)
    };

    public MainWindow()
    {
        App.LogDiag("[MainWindow] ctor begin");
        InitializeComponent();
        App.LogDiag("[MainWindow] ctor end");
        _errorToastTimer.Tick += OnErrorToastTimerTick;
        _statusBlinkTimer.Tick += OnStatusBlinkTick;
        _toolTimer.Tick += OnToolTimerTick;
        _slashDebounceTimer.Tick += OnSlashDebounceTick;
        if (InputTextBox is not null)
            InputTextBox.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _errorToastTimer.Stop();
        _errorToastTimer.Tick -= OnErrorToastTimerTick;
        _statusBlinkTimer.Stop();
        _statusBlinkTimer.Tick -= OnStatusBlinkTick;
        _toolTimer.Stop();
        _toolTimer.Tick -= OnToolTimerTick;
        _slashDebounceTimer.Stop();
        _slashDebounceTimer.Tick -= OnSlashDebounceTick;
        _toastCts?.Cancel();
        _errorToastFadeCts?.Cancel();
        Closed -= OnWindowClosed;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.Messages.CollectionChanged -= OnMessagesChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PermissionConfirmCallback = ShowPermissionDialogAsync;
            _vm.Messages.CollectionChanged += OnMessagesChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    /// <summary>权限确认回调：弹出确认框并把用户决策返回给网关；关闭窗口等价于拒绝</summary>
    private async Task<Hosting.PermissionConfirmationDecision> ShowPermissionDialogAsync(
        Hosting.PermissionConfirmationRequest request)
    {
        var dialog = new PermissionDialog(request);
        return await dialog.ShowDialog<Hosting.PermissionConfirmationDecision>(this);
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
            UpdateStatusBlink();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsBusy))
        {
            if (_vm!.IsBusy)
                _toolTimer.Start();
            else
                _toolTimer.Stop();
        }
        else if (e.PropertyName == nameof(MainViewModel.InputText))
        {
            StartSlashDebounce();
        }
    }

    /// <summary>启动斜杠补全防抖（30ms 后合并刷新）</summary>
    private void StartSlashDebounce()
    {
        _slashDebounceTimer.Stop();
        _slashDebounceTimer.Start();
    }

    /// <summary>防抖到期：同步光标位置并刷新斜杠建议</summary>
    private void OnSlashDebounceTick(object? sender, EventArgs e)
    {
        _slashDebounceTimer.Stop();
        if (_vm is null || InputTextBox is null)
            return;
        _vm.InputCaretIndex = InputTextBox.CaretIndex;
        _vm.RefreshSlashSuggestions();
    }

    /// <summary>根据当前 StatusKind 启停状态点闪烁：Busy 闪烁，Ready/Error 停止并恢复不透明</summary>
    private void UpdateStatusBlink()
    {
        if (_vm is null || StatusDot is null)
            return;
        if (_vm.StatusKind == ViewModels.StatusKind.Busy)
        {
            if (!_statusBlinkTimer.IsEnabled)
            {
                _statusBlinkBright = true;
                StatusDot.Opacity = 1;
                _statusBlinkTimer.Start();
            }
        }
        else
        {
            _statusBlinkTimer.Stop();
            StatusDot.Opacity = 1;
        }
    }

    /// <summary>状态点闪烁 tick：亮暗交替（1.0 ↔ 0.3），让用户感知引擎仍在工作</summary>
    private void OnStatusBlinkTick(object? sender, EventArgs e)
    {
        if (StatusDot is null)
            return;
        _statusBlinkBright = !_statusBlinkBright;
        StatusDot.Opacity = _statusBlinkBright ? 1.0 : 0.3;
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

    /// <summary>新消息加入时，若未上滑浏览则自动滚动到底部</summary>
    private void OnMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
            && _autoScrollEnabled
            && MessageScroll is not null)
        {
            MessageScroll.ScrollToEnd();
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
        if (BackToBottomButton is not null)
            BackToBottomButton.IsVisible = !isNearBottom;
    }

    /// <summary>点击浮钮：跳到底部并恢复自动滚动</summary>
    private void OnBackToBottomClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MessageScroll.ScrollToEnd();
        BackToBottomButton.IsVisible = false;
        _autoScrollEnabled = true;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        // 斜杠命令补全下拉打开时优先响应导航/补全/关闭
        if (vm.IsSlashPopupOpen)
        {
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                vm.SlashNavigate(1);
                return;
            }
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                vm.SlashNavigate(-1);
                return;
            }
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                e.Handled = true;
                vm.CompleteSlashSuggestion();
                FocusInputEnd();
                return;
            }
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                vm.CloseSlashPopup();
                return;
            }
        }

        if (e.Key == Key.Enter)
        {
            var isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            if (isShift)
            {
                e.Handled = true;
                if (sender is TextBox textBox)
                {
                    var caret = textBox.CaretIndex;
                    vm.InputText = textBox.Text!.Insert(caret, "\n");
                    textBox.CaretIndex = caret + 1;
                }
            }
            else if (!vm.IsBusy)
            {
                e.Handled = true;
                vm.SendCommand.Execute(null);
            }
        }
        else if (e.Key == Key.Up && !vm.IsSlashPopupOpen)
        {
            e.Handled = true;
            vm.NavigateHistoryCommand.Execute(-1);
        }
        else if (e.Key == Key.Down && !vm.IsSlashPopupOpen)
        {
            e.Handled = true;
            vm.NavigateHistoryCommand.Execute(1);
        }
        else if (e.Key == Key.Left || e.Key == Key.Right)
        {
            StartSlashDebounce();
        }
    }

    /// <summary>聚焦输入框并把光标移到末尾（命令补全后调用）</summary>
    private void FocusInputEnd()
    {
        if (InputTextBox is null)
            return;
        InputTextBox.Focus();
        InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0;
    }

    /// <summary>点击删除按钮：从会话中移除该消息</summary>
    private void OnRemoveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChatUiMessage message } && DataContext is MainViewModel vm)
        {
            vm.RemoveMessageCommand.Execute(message);
        }
    }

    /// <summary>点击会话删除按钮：移除该会话</summary>
    private void OnSessionRemoveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: SessionItem session } && DataContext is MainViewModel vm)
        {
            vm.RemoveSessionCommand.Execute(session);
        }
    }

    /// <summary>单击会话条目：切换选中态（高亮当前会话）</summary>
    private void OnSessionSingleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Avalonia.StyledElement { DataContext: SessionItem session } && DataContext is MainViewModel vm)
            vm.SelectSessionCommand.Execute(session);
    }

    /// <summary>双击会话条目：请求重命名（实际由 VM 状态驱动内联编辑）</summary>
    private void OnSessionDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Avalonia.StyledElement { DataContext: SessionItem session } && DataContext is MainViewModel vm)
        {
            vm.BeginRenameSessionCommand.Execute(session);
        }
    }

    /// <summary>重命名编辑框按 Enter 提交，Esc 取消</summary>
    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (sender is Avalonia.StyledElement { DataContext: SessionItem session })
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                vm.CommitRenameSessionCommand.Execute(session);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                session.IsRenaming = false;
            }
        }
    }
}