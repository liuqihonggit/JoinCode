namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 资源类型 — 插件持有的界面资源分类
/// </summary>
public enum UiResourceKind {
    /// <summary>
    /// 图标资源
    /// </summary>
    [EnumValue("icon")] Icon,

    /// <summary>
    /// 菜单项资源
    /// </summary>
    [EnumValue("menuitem")] MenuItem,

    /// <summary>
    /// 工具栏按钮资源
    /// </summary>
    [EnumValue("toolbarbutton")] ToolbarButton,

    /// <summary>
    /// 面板资源
    /// </summary>
    [EnumValue("panel")] Panel,

    /// <summary>
    /// 状态栏资源
    /// </summary>
    [EnumValue("statusbar")] StatusBar,
}