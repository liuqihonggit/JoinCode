namespace NativeTui.Core.Repl;

/// <summary>
/// 解析后的按键事件 — 对齐基准 ink 的 (input, key) 二元组：
/// Kind 区分按键种类，Input 携带文本（可打印字符原样 / Ctrl 时小写字母名 / 功能键为空），
/// 大写字母自动置 Shift（基准 parseKey 末段语义）。
/// </summary>
/// <param name="Kind">按键种类。</param>
/// <param name="Input">文本载荷：Char 为字符本身；Ctrl+字母为小写字母名；功能键为空串。</param>
/// <param name="Ctrl">Ctrl 修饰（含 CSI ;5/;6/;7/;8 修饰符）。</param>
/// <param name="Meta">Meta/Alt 修饰（ESC 前缀或 CSI ;3 修饰符）。</param>
/// <param name="Shift">Shift 修饰（大写字母或 CSI ;2 修饰符）。</param>
public sealed record KeyPress(KeyKind Kind, string Input, bool Ctrl = false, bool Meta = false, bool Shift = false)
{
    /// <summary>构造可打印字符按键（大写自动置 Shift）。</summary>
    public static KeyPress Char(string input) =>
        new(KeyKind.Char, input, Shift: input.Length == 1 && input[0] is >= 'A' and <= 'Z');

    /// <summary>构造带修饰的命名按键（Input 空）。</summary>
    public static KeyPress Named(KeyKind kind, bool ctrl = false, bool meta = false, bool shift = false) =>
        new(kind, string.Empty, ctrl, meta, shift);
}
