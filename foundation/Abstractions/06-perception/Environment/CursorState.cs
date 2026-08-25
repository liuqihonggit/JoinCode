namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 光标状态 — 对应 Win32 GetCursorInfo 的光标类型（PRD E-03 异步等待感知）
/// </summary>
public enum CursorState
{
    /// <summary>正常箭头光标</summary>
    Normal,

    /// <summary>等待沙漏（系统级忙，不可交互）</summary>
    Wait,

    /// <summary>箭头+沙漏（应用级忙，可交互但慢）</summary>
    AppStarting,

    /// <summary>帮助光标</summary>
    Help,

    /// <summary>未知/自定义光标</summary>
    Unknown,
}
