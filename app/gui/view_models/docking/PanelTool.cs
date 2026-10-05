namespace JoinCode.Gui.ViewModels.Docking;

/// <summary>
/// 可停靠面板 Tool — 包装现有面板为 Dock.Avalonia 的 Tool，
/// <see cref="Kind"/> 标识面板类型供 <see cref="Views.Docking.PanelViewLocator"/> 选择对应 View。
/// </summary>
public sealed class PanelTool : Tool {
    /// <summary>面板类型标识</summary>
    public SidePanelKind Kind { get; init; }
}
