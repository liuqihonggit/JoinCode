namespace JoinCode.Gui.Views;

/// <summary>
/// 底部面板 UserControl — 输出/终端/问题三标签,可折叠。
/// 纯绑定,数据由 MainViewModel 提供。标题栏支持拖拽停靠到窗口边缘。
/// </summary>
public sealed partial class PanelView : UserControl {
    /// <summary>拖拽起点(屏幕坐标)，null 表示未在拖拽中</summary>
    private Point? _dragStart;

    /// <summary>拖拽累计移动距离</summary>
    private double _dragDistance;

    /// <summary>是否已确认进入拖拽状态(超过阈值)</summary>
    private bool _isDragging;

    /// <summary>初始化 PanelView 实例</summary>
    public PanelView() {
        InitializeComponent();
    }

    /// <summary>标题栏按下 — 记录拖拽起点</summary>
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (e.Pointer.IsPrimary) {
            _dragStart = e.GetPosition(null);
            _dragDistance = 0;
            _isDragging = false;
        }
    }

    /// <summary>标题栏移动 — 累计距离，超过阈值标记为拖拽中</summary>
    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e) {
        if (_dragStart is not { } start)
            return;
        var pos = e.GetPosition(null);
        _dragDistance = Math.Sqrt(Math.Pow(pos.X - start.X, 2) + Math.Pow(pos.Y - start.Y, 2));
        if (_dragDistance >= 20)
            _isDragging = true;
    }

    /// <summary>标题栏释放 — 如果是拖拽，计算停靠位置并切换</summary>
    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e) {
        if (!_isDragging || _dragStart is not { } start)
            return;

        var vm = DataContext as ViewModels.MainViewModel;
        if (vm is null)
            return;

        var window = this.FindAncestorOfType<Window>();
        if (window is null)
            return;

        var releasePos = e.GetPosition(null);
        var windowPos = window.PointToScreen(new Point(0, 0));
        var releaseScreen = new Point(
            releasePos.X - windowPos.X / window.RenderScaling,
            releasePos.Y - windowPos.Y / window.RenderScaling);
        var bounds = window.Bounds;

        var dropPos = vm.ComputePanelDropPosition(
            releaseScreen.X, releaseScreen.Y,
            bounds.Width, bounds.Height,
            _dragDistance);

        if (dropPos is { } pos)
            vm.SetPanelPositionCommand.Execute(pos);

        _dragStart = null;
        _isDragging = false;
        _dragDistance = 0;
    }
}
