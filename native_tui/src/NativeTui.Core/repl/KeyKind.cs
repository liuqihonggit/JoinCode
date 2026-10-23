namespace NativeTui.Core.Repl;

/// <summary>
/// 按键种类 — 对齐基准 ink/events/input-event.ts 的 Key 布尔集合（C# 收敛为枚举）。
/// 非字母数字键（方向键/Home 等）的 Input 为空串，对齐基准 nonAlphanumericKeys 清空语义。
/// </summary>
public enum KeyKind
{
    /// <summary>可打印字符（Input 携带字符；Ctrl 时携带小写字母名）。</summary>
    Char,

    /// <summary>回车（\r / \n）。</summary>
    Return,

    /// <summary>孤立 Esc（chunk 边界语义）。</summary>
    Escape,

    /// <summary>退格（\x7f / \b）。</summary>
    Backspace,

    /// <summary>Delete 键（ESC[3~）。</summary>
    Delete,

    /// <summary>Tab（\t）。</summary>
    Tab,

    /// <summary>Shift+Tab（ESC[Z，ReadKey 路径 Tab+Shift 归一）— 权限模式循环键。</summary>
    BackTab,

    /// <summary>左方向键。</summary>
    LeftArrow,

    /// <summary>上方向键。</summary>
    UpArrow,

    /// <summary>右方向键。</summary>
    RightArrow,

    /// <summary>下方向键。</summary>
    DownArrow,

    /// <summary>Home 键。</summary>
    Home,

    /// <summary>End 键。</summary>
    End,

    /// <summary>PageUp 键。</summary>
    PageUp,

    /// <summary>PageDown 键。</summary>
    PageDown,

    /// <summary>Insert 键（ESC[2~）。</summary>
    Insert,

    /// <summary>Clear 键（键盘中区 5 键 — ESC[E / ESC OE，基准 parse-keypress.ts:354）。</summary>
    Clear,

    /// <summary>F1 功能键（SS3 OP / ESC[11~ / ESC[[A 三变体）。</summary>
    F1,

    /// <summary>F2 功能键。</summary>
    F2,

    /// <summary>F3 功能键。</summary>
    F3,

    /// <summary>F4 功能键。</summary>
    F4,

    /// <summary>F5 功能键（ESC[15~ / ESC[[E）。</summary>
    F5,

    /// <summary>F6 功能键（ESC[17~）。</summary>
    F6,

    /// <summary>F7 功能键（ESC[18~）。</summary>
    F7,

    /// <summary>F8 功能键（ESC[19~）。</summary>
    F8,

    /// <summary>F9 功能键（ESC[20~）。</summary>
    F9,

    /// <summary>F10 功能键（ESC[21~）。</summary>
    F10,

    /// <summary>F11 功能键（ESC[23~）。</summary>
    F11,

    /// <summary>F12 功能键（ESC[24~）。</summary>
    F12,

    /// <summary>bracketed paste 粘贴块（\x1b[200~ 内容 \x1b[201~ — Input 携带粘贴原文，修饰符全 false）。</summary>
    Paste,

    /// <summary>终端获得焦点（DECSET 1004 的 CSI I — 基准 terminal-focus-state.ts setTerminalFocused(true)）。</summary>
    FocusIn,

    /// <summary>终端失去焦点（DECSET 1004 的 CSI O — 失焦暂停动画省 CPU 的驱动信号）。</summary>
    FocusOut,

    /// <summary>SGR 鼠标事件（DECSET 1006 的 ESC[&lt;b;x;y M/m — KeyPress.Mouse 携带结构化 MouseEvent，
    /// 坐标 0-based；RawStdin VT 流专属通道，ReadKey 通道天然无鼠标）。</summary>
    Mouse,

    /// <summary>无法识别的序列（整体吞掉，Input 空 — 防止转义序列泄漏为文本）。</summary>
    Unknown,
}
