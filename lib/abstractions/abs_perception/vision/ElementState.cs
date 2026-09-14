namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 元素状态 — 对应可见性/可用性/交互状态
/// </summary>
public enum ElementState
{
    /// <summary>正常可用</summary>
    [EnumValue("normal")]
    Normal,

    /// <summary>禁用（灰显）</summary>
    [EnumValue("disabled")]
    Disabled,

    /// <summary>已选中</summary>
    [EnumValue("selected")]
    Selected,

    /// <summary>悬停态</summary>
    [EnumValue("hovered")]
    Hovered,

    /// <summary>已聚焦</summary>
    [EnumValue("focused")]
    Focused,

    /// <summary>隐藏</summary>
    [EnumValue("hidden")]
    Hidden,

    /// <summary>按下态</summary>
    [EnumValue("pressed")]
    Pressed,
}
