namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 底部面板标签类型。
/// </summary>
public enum PanelTabKind {
    /// <summary>输出日志</summary>
    Output,
    /// <summary>集成终端</summary>
    Terminal,
    /// <summary>问题/错误列表</summary>
    Problems
}

/// <summary>
/// 面板位置 — 底部/右侧/左侧/顶部。
/// </summary>
public enum PanelPosition {
    /// <summary>底部(默认)</summary>
    Bottom,
    /// <summary>右侧</summary>
    Right,
    /// <summary>左侧</summary>
    Left,
    /// <summary>顶部</summary>
    Top
}

/// <summary>
/// MainViewModel 底部面板 partial — 输出/终端/问题三标签,可折叠,位置可切换(底部/右侧/左侧/顶部)。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>面板默认高度</summary>
    private const double PanelDefaultHeight = 200;

    /// <summary>面板默认宽度(右侧/左侧位置时使用)</summary>
    private const double PanelDefaultWidth = 400;

    /// <summary>面板是否展开</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelTabOutput))]
    [NotifyPropertyChangedFor(nameof(IsPanelTabTerminal))]
    [NotifyPropertyChangedFor(nameof(IsPanelTabProblems))]
    private bool _isPanelOpen;

    /// <summary>当前激活的面板标签</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelTabOutput))]
    [NotifyPropertyChangedFor(nameof(IsPanelTabTerminal))]
    [NotifyPropertyChangedFor(nameof(IsPanelTabProblems))]
    private PanelTabKind _activePanelTab = PanelTabKind.Output;

    /// <summary>面板位置 — 底部/右侧/左侧/顶部</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelBottom))]
    [NotifyPropertyChangedFor(nameof(IsPanelRight))]
    [NotifyPropertyChangedFor(nameof(IsPanelLeft))]
    [NotifyPropertyChangedFor(nameof(IsPanelTop))]
    private PanelPosition _panelPosition = PanelPosition.Bottom;

    /// <summary>面板高度(底部/顶部位置时使用)</summary>
    [ObservableProperty]
    private double _panelHeight = PanelDefaultHeight;

    /// <summary>面板宽度(左侧/右侧位置时使用)</summary>
    [ObservableProperty]
    private double _panelWidth = PanelDefaultWidth;

    /// <summary>面板在底部</summary>
    public bool IsPanelBottom => IsPanelOpen && PanelPosition == PanelPosition.Bottom;

    /// <summary>面板在右侧</summary>
    public bool IsPanelRight => IsPanelOpen && PanelPosition == PanelPosition.Right;

    /// <summary>面板在左侧</summary>
    public bool IsPanelLeft => IsPanelOpen && PanelPosition == PanelPosition.Left;

    /// <summary>面板在顶部</summary>
    public bool IsPanelTop => IsPanelOpen && PanelPosition == PanelPosition.Top;

    /// <summary>输出标签是否激活</summary>
    public bool IsPanelTabOutput => IsPanelOpen && ActivePanelTab == PanelTabKind.Output;

    /// <summary>终端标签是否激活</summary>
    public bool IsPanelTabTerminal => IsPanelOpen && ActivePanelTab == PanelTabKind.Terminal;

    /// <summary>问题标签是否激活</summary>
    public bool IsPanelTabProblems => IsPanelOpen && ActivePanelTab == PanelTabKind.Problems;

    /// <summary>输出文本内容</summary>
    [ObservableProperty]
    private string _panelOutputText = "";

    /// <summary>切换面板标签</summary>
    [RelayCommand]
    private void SwitchPanelTab(string? tab) {
        if (tab is null)
            return;
        ActivePanelTab = tab switch {
            "Output" => PanelTabKind.Output,
            "Terminal" => PanelTabKind.Terminal,
            "Problems" => PanelTabKind.Problems,
            _ => ActivePanelTab
        };
        IsPanelOpen = true;
    }

    /// <summary>折叠/展开面板</summary>
    [RelayCommand]
    private void TogglePanel() {
        IsPanelOpen = !IsPanelOpen;
    }

    /// <summary>切换面板位置 — 在底部/右侧/左侧/顶部之间循环</summary>
    [RelayCommand]
    private void CyclePanelPosition() {
        PanelPosition = PanelPosition switch {
            PanelPosition.Bottom => PanelPosition.Right,
            PanelPosition.Right => PanelPosition.Left,
            PanelPosition.Left => PanelPosition.Top,
            PanelPosition.Top => PanelPosition.Bottom,
            _ => PanelPosition.Bottom
        };
        IsPanelOpen = true;
    }

    /// <summary>设置面板位置</summary>
    [RelayCommand]
    private void SetPanelPosition(PanelPosition? position) {
        if (position is not null)
            PanelPosition = position.Value;
        IsPanelOpen = true;
    }

    /// <summary>追加输出文本</summary>
    public void AppendPanelOutput(string text) {
        PanelOutputText += text + "\n";
    }

    /// <summary>拖拽生效的最小移动距离(像素) — 小于此值视为点击而非拖拽</summary>
    private const double PanelDragThreshold = 20;

    /// <summary>停靠区域的边缘占比 — 终点落在窗口边缘 1/3 范围内才停靠</summary>
    private const double PanelDockEdgeRatio = 1.0 / 3.0;

    /// <summary>
    /// 根据拖拽终点相对于窗口边缘的位置，计算面板应停靠的位置。
    /// 返回 null 表示不停靠(拖拽距离不足或终点远离所有边缘)。
    /// </summary>
    /// <param name="x">拖拽终点 X 坐标(相对于窗口左上角)</param>
    /// <param name="y">拖拽终点 Y 坐标(相对于窗口左上角)</param>
    /// <param name="width">窗口宽度</param>
    /// <param name="height">窗口高度</param>
    /// <param name="dragDistance">拖拽总移动距离(像素)，默认 double.MaxValue 表示已确认是拖拽</param>
    /// <returns>停靠位置或 null</returns>
    public PanelPosition? ComputePanelDropPosition(double x, double y, double width, double height, double dragDistance = double.MaxValue) {
        if (dragDistance < PanelDragThreshold)
            return null;

        var distTop = y;
        var distBottom = height - y;
        var distLeft = x;
        var distRight = width - x;

        var thresholdH = height * PanelDockEdgeRatio;
        var thresholdW = width * PanelDockEdgeRatio;

        var candidates = new (double Dist, double Threshold, PanelPosition Pos)[] {
            (distTop,    thresholdH, PanelPosition.Top),
            (distBottom, thresholdH, PanelPosition.Bottom),
            (distLeft,   thresholdW, PanelPosition.Left),
            (distRight,  thresholdW, PanelPosition.Right),
        };

        var best = candidates.OrderBy(c => c.Dist).First();
        return best.Dist <= best.Threshold ? best.Pos : null;
    }
}
