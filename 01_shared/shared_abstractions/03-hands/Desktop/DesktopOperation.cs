namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 桌面操作种类 — 标识原子操作类型，用于操作日志与回放
/// </summary>
public enum DesktopOperationKind
{
    /// <summary>鼠标移动</summary>
    Move,

    /// <summary>鼠标点击</summary>
    Click,

    /// <summary>拖拽</summary>
    Drag,

    /// <summary>按键</summary>
    KeyPress,

    /// <summary>文本输入</summary>
    TypeText,

    /// <summary>窗口激活</summary>
    WindowFocus,

    /// <summary>窗口移动/调整</summary>
    WindowMove,

    /// <summary>窗口关闭</summary>
    WindowClose,

    /// <summary>截图</summary>
    Screenshot,
}

/// <summary>
/// 桌面操作原子单元 — 可回放、可审计，为 P4 宏录制/P5 观察学习铺垫
/// </summary>
public sealed record DesktopOperation(
    DesktopOperationKind Kind,
    int X,
    int Y,
    string? Text,
    MouseAction? MouseAction,
    KeyModifier? Modifiers,
    DateTimeOffset Timestamp,
    bool Succeeded,
    string? Error);
