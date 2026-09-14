namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 状态三态类别 — 驱动顶栏状态指示器配色（就绪绿 / 思考黄 / 错误红）。
/// </summary>
public enum StatusKind
{
    /// <summary>就绪（绿色）</summary>
    Ready,
    /// <summary>思考中（黄色）</summary>
    Busy,
    /// <summary>错误（红色）</summary>
    Error
}
