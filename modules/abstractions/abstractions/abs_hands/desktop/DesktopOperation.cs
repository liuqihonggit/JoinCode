namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 桌面操作种类 — 标识原子操作类型，用于操作日志与回放
/// </summary>
public enum DesktopOperationKind {
    /// <summary>鼠标移动</summary>
    [EnumValue("move")] Move,

    /// <summary>鼠标点击</summary>
    [EnumValue("click")] Click,

    /// <summary>拖拽</summary>
    [EnumValue("drag")] Drag,

    /// <summary>按键</summary>
    [EnumValue("key_press")] KeyPress,

    /// <summary>文本输入</summary>
    [EnumValue("type_text")] TypeText,

    /// <summary>窗口激活</summary>
    [EnumValue("window_focus")] WindowFocus,

    /// <summary>窗口移动/调整</summary>
    [EnumValue("window_move")] WindowMove,

    /// <summary>窗口关闭</summary>
    [EnumValue("window_close")] WindowClose,

    /// <summary>截图</summary>
    [EnumValue("screenshot")] Screenshot,
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