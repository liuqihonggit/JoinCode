namespace JoinCode.Gui.Views;

/// <summary>
/// 底部输入栏 UserControl — composer 卡片（透明无边框 TextBox 内嵌 + 发送按钮嵌入卡片右下）
/// + 字符计数 + 停止/发送按钮 + 分隔线/时间戳快捷按钮。
/// 斜杠补全面板在 <see cref="SlashPaletteView"/>（MainWindow 布局行，位于本组件正上方同列）；
/// 本组件负责键盘事件（Enter 发送/Up-Down 历史导航/补全导航）与 30ms 输入防抖。
/// </summary>
public sealed partial class InputBarView : UserControl
{
    /// <summary>斜杠命令补全防抖计时器 — 30ms 内多次输入/光标变化合并为一次刷新</summary>
    private readonly DispatcherTimer _slashDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(30)
    };

    private MainViewModel? _vm;

    public InputBarView()
    {
        InitializeComponent();
        _slashDebounceTimer.Tick += OnSlashDebounceTick;
        if (InputTextBox is not null)
            InputTextBox.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>当前 MainViewModel（供外部访问）</summary>
    public MainViewModel? ViewModel => _vm;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.InputText))
            StartSlashDebounce();
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

    /// <summary>聚焦输入框并把光标移到末尾（命令补全后由宿主调用）</summary>
    public void FocusInput()
    {
        if (InputTextBox is null)
            return;
        InputTextBox.Focus();
        InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        // 用户打字重置子代理空闲倒计时（任何按键都算活动，别移交 mainAgent）
        vm.ResetIdleTimer();

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
                FocusInput();
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
            // F3 快捷键面板驱动：EnterSends=true → Enter 发送；false → Ctrl+Enter 发送、Enter 换行
            var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            var sendPressed = vm.EnterSends ? !shift : ctrl;

            if (sendPressed)
            {
                if (!vm.IsBusy)
                {
                    e.Handled = true;
                    // 用户发送消息取消空闲倒计时 — 主动接管，立即恢复子代理
                    vm.StopIdleTimer();
                    vm.SendCommand.Execute(null);
                }
            }
            else
            {
                e.Handled = true;
                if (sender is TextBox textBox)
                {
                    var caret = textBox.CaretIndex;
                    vm.InputText = textBox.Text!.Insert(caret, "\n");
                    textBox.CaretIndex = caret + 1;
                }
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

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _slashDebounceTimer.Stop();
        _slashDebounceTimer.Tick -= OnSlashDebounceTick;
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        if (InputTextBox is not null)
            InputTextBox.RemoveHandler(InputElement.KeyDownEvent, OnInputKeyDown);
        base.OnDetachedFromVisualTree(e);
    }
}
