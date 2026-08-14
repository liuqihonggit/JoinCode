using Avalonia.Controls;
using Avalonia.Input;

using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Views;

/// <summary>
/// 底部输入栏 UserControl — 输入框 + 字符计数 + 停止/发送按钮 +
/// 斜杠命令补全 Popup + 分隔线/时间戳快捷按钮。
/// 键盘事件（Enter 发送/Up-Down 历史导航/斜杠补全导航）和防抖计时器在本组件内处理。
/// </summary>
public sealed partial class InputBarView : UserControl
{
    /// <summary>斜杠命令补全防抖计时器 — 30ms 内多次输入/光标变化合并为一次刷新</summary>
    private readonly Avalonia.Threading.DispatcherTimer _slashDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(30)
    };

    private MainViewModel? _vm;

    public InputBarView()
    {
        InitializeComponent();
        _slashDebounceTimer.Tick += OnSlashDebounceTick;
        if (InputTextBox is not null)
        {
            InputTextBox.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            InputTextBox.SizeChanged += OnInputSizeChanged;
        }
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
        UpdateSlashPopupWidth();
    }

    /// <summary>输入框尺寸变化时同步补全面板宽度</summary>
    private void OnInputSizeChanged(object? sender, Avalonia.Controls.SizeChangedEventArgs e) => UpdateSlashPopupWidth();

    /// <summary>补全面板宽度对齐输入框实际宽度</summary>
    private void UpdateSlashPopupWidth()
    {
        if (SlashPopupBorder is null || InputTextBox is null)
            return;
        var width = InputTextBox.Bounds.Width;
        if (width > 0)
            SlashPopupBorder.Width = width;
    }

    /// <summary>强制 Popup 重新计算位置 — 通过微调 VerticalOffset 触发内部位置更新</summary>
    public void RepositionSlashPopup()
    {
        if (SlashPopup is not { IsOpen: true } popup)
            return;
        var offset = popup.VerticalOffset;
        popup.VerticalOffset = offset + 0.1;
        popup.VerticalOffset = offset;
    }

    /// <summary>聚焦输入框并把光标移到末尾（命令补全后调用）</summary>
    private void FocusInputEnd()
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

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _slashDebounceTimer.Stop();
        _slashDebounceTimer.Tick -= OnSlashDebounceTick;
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        if (InputTextBox is not null)
        {
            InputTextBox.RemoveHandler(InputElement.KeyDownEvent, OnInputKeyDown);
            InputTextBox.SizeChanged -= OnInputSizeChanged;
        }
        base.OnDetachedFromVisualTree(e);
    }
}
