namespace NativeTui.Core.Repl;

/// <summary>
/// Console.ReadKey 适配器 — 把 ConsoleKeyInfo 归一到 KeyPress 统一模型：
/// KeyChar 控制字符（\x01-\x1a）推断 Ctrl+字母，功能键按 Key 枚举映射，
/// 可打印字符透传（大写自动 Shift）。VT 输入路径走 KeyPressParser，本适配器服务 ReadKey 路径。
/// bracketed paste 说明：ReadKey 逐键到达时粘贴标记序列会被拆散为多个 KeyInfo，
/// 调用方须累积原始 KeyChar 重组 VT 流喂 KeyPressParser（或用 Paste 工厂组合已收集的粘贴文本）。
/// </summary>
public static class ConsoleKeyAdapter
{
    /// <summary>构造粘贴事件（ReadKey 路径调用方收集到完整粘贴文本后组合 — Input 携带粘贴原文）。</summary>
    public static KeyPress Paste(string content) => new(KeyKind.Paste, content);

    /// <summary>归一 ConsoleKeyInfo 为 KeyPress；无法识别返回 null（调用方忽略）。</summary>
    public static KeyPress? ToKeyPress(ConsoleKeyInfo info)
    {
        var ctrl = (info.Modifiers & ConsoleModifiers.Control) != 0;
        var meta = (info.Modifiers & ConsoleModifiers.Alt) != 0;
        var shift = (info.Modifiers & ConsoleModifiers.Shift) != 0;

        // KeyChar 为控制字母（Ctrl+字母：KeyChar = 字母序号）→ Ctrl+字母名
        var ch = info.KeyChar;
        if (ch is >= '\u0001' and <= '\u001a' and not '\u0008' and not '\u0009' and not '\u000d')
        {
            var letter = (char)('a' + ch - 1);
            return new KeyPress(KeyKind.Char, letter.ToString(), Ctrl: true, Meta: meta);
        }

        // 功能键按 Key 枚举映射（KeyChar 通常为 '\0' 或 ESC）
        // Tab + Shift 归一 BackTab（VT 路径 ESC[Z 的 ReadKey 退化形态）
        var kind = info.Key switch
        {
            ConsoleKey.Enter => KeyKind.Return,
            ConsoleKey.Backspace => KeyKind.Backspace,
            ConsoleKey.Delete => KeyKind.Delete,
            ConsoleKey.Tab when shift => KeyKind.BackTab,
            ConsoleKey.Tab => KeyKind.Tab,
            ConsoleKey.Escape => KeyKind.Escape,
            ConsoleKey.LeftArrow => KeyKind.LeftArrow,
            ConsoleKey.RightArrow => KeyKind.RightArrow,
            ConsoleKey.UpArrow => KeyKind.UpArrow,
            ConsoleKey.DownArrow => KeyKind.DownArrow,
            ConsoleKey.Home => KeyKind.Home,
            ConsoleKey.End => KeyKind.End,
            ConsoleKey.PageUp => KeyKind.PageUp,
            ConsoleKey.PageDown => KeyKind.PageDown,
            _ => KeyKind.Unknown,
        };
        if (kind != KeyKind.Unknown)
        {
            return KeyPress.Named(kind, ctrl, meta, shift);
        }

        // 可打印字符透传（大写补 Shift，对齐基准 parseKey 末段）
        if (ch >= ' ')
        {
            return new KeyPress(KeyKind.Char, ch.ToString(), Ctrl: ctrl, Meta: meta,
                Shift: shift || ch is >= 'A' and <= 'Z');
        }

        return null;
    }
}
