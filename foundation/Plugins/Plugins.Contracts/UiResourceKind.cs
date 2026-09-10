namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 资源类型 — 插件持有的界面资源分类
/// </summary>
public enum UiResourceKind
{
    [EnumValue("icon")] Icon,
    [EnumValue("menuitem")] MenuItem,
    [EnumValue("toolbarbutton")] ToolbarButton,
    [EnumValue("panel")] Panel,
    [EnumValue("statusbar")] StatusBar,
}
