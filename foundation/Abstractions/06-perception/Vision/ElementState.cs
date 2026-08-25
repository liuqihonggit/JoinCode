namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 元素状态 — 对应可见性/可用性/交互状态
/// </summary>
public enum ElementState
{
    /// <summary>正常可用</summary>
    Normal,

    /// <summary>禁用（灰显）</summary>
    Disabled,

    /// <summary>已选中</summary>
    Selected,

    /// <summary>悬停态</summary>
    Hovered,

    /// <summary>已聚焦</summary>
    Focused,

    /// <summary>隐藏</summary>
    Hidden,

    /// <summary>按下态</summary>
    Pressed,
}
