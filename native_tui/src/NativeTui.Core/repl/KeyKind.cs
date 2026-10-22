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

    /// <summary>bracketed paste 粘贴块（\x1b[200~ 内容 \x1b[201~ — Input 携带粘贴原文，修饰符全 false）。</summary>
    Paste,

    /// <summary>无法识别的序列（整体吞掉，Input 空 — 防止转义序列泄漏为文本）。</summary>
    Unknown,
}
