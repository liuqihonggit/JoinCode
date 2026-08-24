using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Views;

/// <summary>
/// 底部升起式斜杠补全面板 — 由 MainWindow 放在输入栏正上方的布局行（与输入栏同列约束），
/// 左右边缘天然对齐、物理零重叠；关闭时 IsVisible=False 零占位，打开时将消息区向上顶起。
/// 开合动画为 Task.Delay 步进插值（经 UI 线程同步上下文回投，headless 与桌面行为一致）；
/// 点击候选项触发 <see cref="Completed"/> 事件，由宿主回焦输入框。
/// </summary>
public sealed partial class SlashPaletteView : UserControl
{
    /// <summary>面板滑出动画起始位移（px）— 从输入栏背后向上滑到 0</summary>
    private const double PaletteSlideOffset = 14;

    /// <summary>点击候选项完成补全后触发（宿主负责回焦输入框）</summary>
    public static readonly RoutedEvent<RoutedEventArgs> CompletedEvent =
        RoutedEvent.Register<SlashPaletteView, RoutedEventArgs>(nameof(Completed), RoutingStrategies.Bubble);

    /// <summary>点击候选项完成补全后触发</summary>
    public event EventHandler<RoutedEventArgs> Completed
    {
        add => AddHandler(CompletedEvent, value);
        remove => RemoveHandler(CompletedEvent, value);
    }

    /// <summary>动画代际计数 — 新开合请求使进行中的旧动画循环失效</summary>
    private uint _animGeneration;

    /// <summary>当前动画方向（true=展开）</summary>
    private bool _animOpening;

    private MainViewModel? _vm;

    public SlashPaletteView()
    {
        InitializeComponent();
        if (PaletteList is not null)
            PaletteList.Tapped += OnPaletteListTapped;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            ApplyOpenState(_vm.IsSlashPopupOpen, animate: false);
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSlashPopupOpen))
            ApplyOpenState(_vm?.IsSlashPopupOpen ?? false, animate: true);
        else if (e.PropertyName == nameof(MainViewModel.SlashSelectedIndex))
            ScrollSuggestionIntoView();
    }

    /// <summary>面板开合 — IsVisible 管布局占位；animate=false 用于初始同步（跳过动画直接落位）</summary>
    private void ApplyOpenState(bool open, bool animate)
    {
        var root = PaletteRoot;
        if (root is null)
            return;
        root.IsHitTestVisible = open;
        _animGeneration++;
        if (!open)
        {
            if (!animate || root.Opacity <= 0)
            {
                root.Opacity = 0;
                root.IsVisible = false;
                return;
            }
        }
        else
        {
            root.IsVisible = true;
        }
        _animOpening = open;
        _ = RunPaletteAnimationAsync();
    }

    /// <summary>
    /// 动画循环 — 15ms/帧、140ms 完成、CubicEaseOut；await 经 UI 线程同步上下文回投，
    /// headless 调度器与桌面合成器均能推进。代际不匹配即退出（新请求已接管）。
    /// </summary>
    private async System.Threading.Tasks.Task RunPaletteAnimationAsync()
    {
        var gen = _animGeneration;
        const double totalMs = 140;
        for (double ms = 0; ms <= totalMs; ms += 15)
        {
            if (gen != _animGeneration)
                return;
            ApplyAnimationFrame(Math.Min(ms / totalMs, 1.0));
            await System.Threading.Tasks.Task.Delay(15).ConfigureAwait(true);
        }
        if (gen != _animGeneration)
            return;
        var root = PaletteRoot;
        if (root is null)
            return;
        root.Opacity = _animOpening ? 1 : 0;
        if (!_animOpening)
            root.IsVisible = false;
    }

    /// <summary>按进度 t∈[0,1] 应用一帧（透明度 0↔1、translateY 14px↔0，CubicEaseOut）</summary>
    private void ApplyAnimationFrame(double t)
    {
        var root = PaletteRoot;
        if (root is null)
            return;
        double eased = 1 - Math.Pow(1 - t, 3);
        root.Opacity = _animOpening ? eased : 1 - eased;
        double y = (_animOpening ? PaletteSlideOffset : 0) * (1 - eased);
        root.RenderTransform = TransformOperations.Parse($"translateY({y:F2}px)");
    }

    /// <summary>选中项滚动进可视区（↑↓ 长列表导航时列表跟随）</summary>
    private void ScrollSuggestionIntoView()
    {
        if (_vm is not { SlashSelectedIndex: >= 0 } vm || PaletteList is null)
            return;
        if (vm.SlashSelectedIndex >= vm.SlashSuggestions.Count)
            return;
        PaletteList.ScrollIntoView(vm.SlashSuggestions[vm.SlashSelectedIndex]);
        // ScrollIntoView 对末项可能差数像素（margin/padding 舍入），布局完成后几何校正确保完全可见
        var index = vm.SlashSelectedIndex;
        Dispatcher.UIThread.Post(() => EnsureContainerFullyVisible(index), DispatcherPriority.Loaded);
    }

    /// <summary>几何校正：把选中容器完整滚入视口（顶部溢出上滚 / 底部溢出下滚）</summary>
    private void EnsureContainerFullyVisible(int index)
    {
        if (PaletteList?.ContainerFromIndex(index) is not Visual container)
            return;
        var scroll = PaletteList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null || scroll.Extent.Height <= scroll.Viewport.Height)
            return;
        var top = (container.TransformToVisual(scroll) ?? default).Transform(default).Y;
        var bottom = top + container.Bounds.Height;
        double dy = top < 0 ? top
            : bottom > scroll.Viewport.Height ? bottom - scroll.Viewport.Height
            : 0;
        if (Math.Abs(dy) > 0.5)
            scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + dy);
    }

    /// <summary>点击候选项：补全并通知宿主回焦输入框</summary>
    private void OnPaletteListTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        vm.CompleteSlashSuggestion();
        RaiseEvent(new RoutedEventArgs(CompletedEvent));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _animGeneration++;
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        if (PaletteList is not null)
            PaletteList.Tapped -= OnPaletteListTapped;
        base.OnDetachedFromVisualTree(e);
    }
}

