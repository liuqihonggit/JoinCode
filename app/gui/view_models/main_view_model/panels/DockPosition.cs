namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 面板停靠位置 — 每个面板可停靠在左侧/右侧/底部,或浮动为独立窗口。
/// 拖拽面板标题栏到窗口边缘切换停靠位置(参考 VSCode)。
/// </summary>
public enum DockPosition {
    /// <summary>停靠在左侧 SideBar(默认)</summary>
    Left,
    /// <summary>停靠在右侧 SecondarySideBar</summary>
    Right,
    /// <summary>停靠在底部 Panel 区</summary>
    Bottom,
    /// <summary>浮动为独立窗口(暂未实现)</summary>
    Float
}

/// <summary>
/// 面板注册信息 — 描述一个可停靠面板的元数据。
/// </summary>
/// <param name="Kind">面板类型</param>
/// <param name="Title">面板标题(显示在标题栏)</param>
/// <param name="Icon">面板图标(显示在 ActivityBar)</param>
/// <param name="DefaultDock">默认停靠位置</param>
public sealed record PanelDescriptor(SidePanelKind Kind, string Title, string Icon, DockPosition DefaultDock = DockPosition.Left);
