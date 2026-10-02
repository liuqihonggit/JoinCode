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
}
