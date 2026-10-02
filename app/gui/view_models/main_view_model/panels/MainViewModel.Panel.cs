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
/// MainViewModel 底部面板 partial — 输出/终端/问题三标签,可折叠。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>面板默认高度</summary>
    private const double PanelDefaultHeight = 200;

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

    /// <summary>面板高度</summary>
    [ObservableProperty]
    private double _panelHeight = PanelDefaultHeight;

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

    /// <summary>追加输出文本</summary>
    public void AppendPanelOutput(string text) {
        PanelOutputText += text + "\n";
    }
}
