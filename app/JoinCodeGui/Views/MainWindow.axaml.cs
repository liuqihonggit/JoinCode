using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using JoinCode.Gui.Theming;
using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Views;

/// <summary>
/// 主窗口 code-behind — 仅承载视图逻辑（输入回车发送、新消息自动滚动到底），业务均走 ViewModel。
/// </summary>
public sealed partial class MainWindow : Window
{
    private MainViewModel? _vm;

    private System.Threading.CancellationTokenSource? _toastCts;

    private bool _autoScrollEnabled = true;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
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
        else if (e.PropertyName == nameof(MainViewModel.ExportedSessionCopy) && !string.IsNullOrEmpty(_vm!.ExportedSessionCopy))
        {
            Clipboard?.SetTextAsync(_vm.ExportedSessionCopy);
            _vm.ClearSessionExport();
            ScheduleCopyToastHide();
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

    /// <summary>新消息加入时，若未上滑浏览则自动滚动到底部</summary>
    private void OnMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && _autoScrollEnabled)
            MessageScroll.ScrollToEnd();
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
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (!vm.IsBusy)
                vm.SendCommand.Execute(null);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            vm.NavigateHistoryCommand.Execute(-1);
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            vm.NavigateHistoryCommand.Execute(1);
        }
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