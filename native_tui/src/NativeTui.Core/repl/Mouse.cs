namespace NativeTui.Core.Repl;

/// <summary>
/// 鼠标事件种类 — 对齐基准 ink/parse-keypress.ts:568-583 ParsedMouse（press/release）
/// 与 :683-684 滚轮 nav key（wheelup/wheeldown，0x43 掩码判定）；
/// WheelLeft/WheelRight（b=66/67）为 SGR（DECSET 1006）规范扩展 —
/// 基准 :686 将两方向归入 'mouse' 名占位，w2 显式建模为独立种类。
/// </summary>
public enum MouseKind
{
    /// <summary>按钮按下（终点 'M'，无 motion/wheel 位）。</summary>
    Press,

    /// <summary>按钮释放（终点 'm'）。</summary>
    Release,

    /// <summary>滚轮向上（b &amp; 0x43 == 0x40，含 64/68 等 — 修饰位忽略，基准 :683）。</summary>
    WheelUp,

    /// <summary>滚轮向下（b &amp; 0x43 == 0x41，含 65/69 等 — 修饰位忽略，基准 :684）。</summary>
    WheelDown,

    /// <summary>滚轮向左（b &amp; 0x43 == 0x42 — SGR 规范扩展，基准归 'mouse' 占位）。</summary>
    WheelLeft,

    /// <summary>滚轮向右（b &amp; 0x43 == 0x43 — SGR 规范扩展，基准归 'mouse' 占位）。</summary>
    WheelRight,

    /// <summary>按住拖拽（motion 位 0x20 + 终点 'M' + 按钮位 0-2，基准 :63 注释 left-drag）。</summary>
    Drag,

    /// <summary>无按钮移动（motion 位 0x20 + 终点 'M' + 按钮位 3 — 纯 hover 移动，需 DECSET 1003 追踪）。</summary>
    Move,
}

/// <summary>
/// 鼠标按钮 — SGR 编码 b 的低 2 位（0=Left 1=Middle 2=Right，基准 parse-keypress.ts:573-574 注释
/// "Low 2 bits = button (0=left,1=mid,2=right)"）；
/// 按钮位 3（无按钮）与滚轮事件时无语义（占位 Left），消费方按 MouseKind 分派。
/// </summary>
public enum MouseButton
{
    /// <summary>左键（b 低 2 位 = 0）。</summary>
    Left,

    /// <summary>中键（b 低 2 位 = 1）。</summary>
    Middle,

    /// <summary>右键（b 低 2 位 = 2）。</summary>
    Right,
}

/// <summary>
/// SGR 鼠标事件（DECSET 1006 编码）— 对齐基准 ink/parse-keypress.ts:571-583 ParsedMouse
/// + events/click-event.ts:10-37 ClickEvent 的坐标语义：
/// 终端序列坐标 1-based，此处归一为 0-based（click-event.ts:11-12 "0-indexed screen column/row"）。
/// </summary>
/// <param name="Kind">事件种类（终点 M/m + motion/wheel 位判定）。</param>
/// <param name="Button">按钮（b 低 2 位映射；无按钮(3)/滚轮时占位 Left 无语义）。</param>
/// <param name="Col">0-based 屏幕列（SGR x - 1）。</param>
/// <param name="Row">0-based 屏幕行（SGR y - 1）。</param>
/// <param name="Shift">Shift 修饰（b bit2 = 0x04，基准 :678 注释）。</param>
/// <param name="Meta">Meta 修饰（b bit3 = 0x08，基准 :678 注释）。</param>
/// <param name="Ctrl">Ctrl 修饰（b bit4 = 0x10，基准 :679 注释）。</param>
public readonly record struct MouseEvent(
    MouseKind Kind,
    MouseButton Button,
    int Col,
    int Row,
    bool Shift,
    bool Meta,
    bool Ctrl);
