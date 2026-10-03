namespace JoinCode.Gui.Views;

/// <summary>
/// 统计面板 — 统一展示会话统计信息（消息数/会话数/字符数/Token数/消息分布）。
/// 消除搜索栏和状态栏中的重复显示，用可视化卡片+进度条统一呈现。
/// </summary>
public sealed partial class StatsPanelView : UserControl {
    /// <summary>初始化 StatsPanelView 实例</summary>
    public StatsPanelView() => InitializeComponent();

    /// <summary>鼠标离开时自动关闭统计面板</summary>
    private void OnPointerExited(object? sender, Avalonia.Input.PointerEventArgs e) {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.IsStatsPanelOpen = false;
    }
}
