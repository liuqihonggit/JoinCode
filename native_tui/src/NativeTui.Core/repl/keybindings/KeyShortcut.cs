namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 快捷键值类型 — 对齐基准 keybindings/parser.ts parseKeystroke：
/// '+' 分段（修饰 ctrl/shift/alt/meta 别名 + 一个主键段），w2 键模型三修饰（Ctrl/Meta/Shift —
/// 无 Super：基准 cmd/super 仅 kitty 键盘协议终端可达，w2 KeyPress 不产出该修饰）。
/// 匹配语义对齐基准 match.ts：Char 键名小写比较 + Shift 修饰全等（'A' 命中 shift+a）；
/// Escape 忽略 Meta（基准 match.ts:96-105 quirk — Ink 对 escape 置 meta，w2 对应 ESC ESC 第二键带 Meta）。
/// 归一特例：shift+tab / backtab → BackTab 且 Shift=true（w2 KeyPressParser 把 ESC[Z 归一为 BackTab，
/// 恒带 Shift — 对齐 KeyPress.cs BackTab 注释的 ReadKey 归一语义）。
/// </summary>
/// <param name="Kind">主键种类（Char 或功能键；Paste/FocusIn/FocusOut/Unknown 不可绑定，解析层拒绝）。</param>
/// <param name="Input">Char 主键载荷（单字符小写；空格为 " "）；功能键为空串。</param>
/// <param name="Ctrl">Ctrl 修饰。</param>
/// <param name="Meta">Meta/Alt 修饰（用户可写 alt/opt/option/meta，w2 单 Meta 槽 — 对齐基准 alt/meta 同映射 key.meta）。</param>
/// <param name="Shift">Shift 修饰（BackTab 恒 true）。</param>
public readonly record struct KeyShortcut(KeyKind Kind, string Input, bool Ctrl, bool Meta, bool Shift)
{
    /// <summary>
    /// 解析快捷键串（"ctrl+x"/"shift+tab"/"escape"/"up"/"alt+enter"）。
    /// 失败抛 <see cref="FormatException"/>（错误文本含拦截层/原因/正确写法）— 默认表等常量场景用；
    /// 用户输入走 <see cref="TryParse(string, out KeyShortcut, out string)"/> 收集校验错误。
    /// </summary>
    /// <param name="text">快捷键串。</param>
    /// <exception cref="FormatException">键名/修饰符/结构非法。</exception>
    public static KeyShortcut Parse(string text) =>
        TryParse(text, out var shortcut, out var error) ? shortcut : throw new FormatException(error);

    /// <summary>
    /// 尝试解析快捷键串：'+' 分段 → 修饰段归一（ctrl/control、alt/opt/option/meta、shift）→ 主键段查键名表
    /// （escape/esc、enter/return、tab、backtab、backspace、delete、up/down/left/right[含 ↑↓←→]、home、end、
    /// pageup、pagedown、space 或单个非控制字符）。
    /// </summary>
    /// <param name="text">快捷键串。</param>
    /// <param name="shortcut">解析结果（失败时为 default）。</param>
    /// <param name="error">失败原因（四要素：哪个值错/为什么/KeyShortcut 解析层拦截/正确写法示例）。</param>
    /// <returns>解析是否成功。</returns>
    public static bool TryParse(string text, out KeyShortcut shortcut, out string error)
    {
        shortcut = default;
        error = string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            error = "快捷键为空串（KeyShortcut 解析层拦截）：空段无法构成键位。正确写法：键名或修饰符+键名，示例 \"ctrl+x\"、\"escape\"";
            return false;
        }

        var ctrl = false;
        var meta = false;
        var shift = false;
        string? keyName = null;
        foreach (var rawPart in text.Split('+'))
        {
            var part = rawPart.Trim().ToLowerInvariant();
            if (part.Length == 0)
            {
                error = $"空键段（\"{text}\"，KeyShortcut 解析层拦截）：'+' 两侧出现空白段（连写 \"ctrl++\" 或结尾 \"ctrl+\"）。正确写法：去掉多余 '+'，示例 \"ctrl+x\"";
                return false;
            }

            switch (part)
            {
                case "ctrl" or "control":
                    ctrl = true;
                    break;
                case "alt" or "opt" or "option" or "meta":
                    meta = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "cmd" or "command" or "super" or "win":
                    error = $"修饰符 \"{rawPart.Trim()}\" 不受支持（KeyShortcut 解析层拦截）：w2 KeyPress 键模型只有 Ctrl/Meta/Shift 三修饰（基准 cmd/super 仅 kitty 键盘协议终端可达，w2 不产出）。请改用 meta（终端 Alt/ESC 前缀）或 ctrl，示例 \"meta+p\"";
                    return false;
                default:
                    if (keyName is not null)
                    {
                        error = $"键名段冲突（\"{rawPart.Trim()}\"，KeyShortcut 解析层拦截）：一个快捷键只能有一个主键（\"{keyName}\" 已占据），多段非修饰文本不是合法键位。正确写法：修饰符+单键名，示例 \"ctrl+shift+up\"";
                        return false;
                    }

                    keyName = part;
                    break;
            }
        }

        if (keyName is null)
        {
            error = $"缺少主键（\"{text}\"，KeyShortcut 解析层拦截）：只有修饰符没有键名。正确写法：修饰符+键名，示例 \"ctrl+x\"、\"shift+tab\"";
            return false;
        }

        return TryBuild(text, keyName, ctrl, meta, shift, ref shortcut, out error);
    }

    /// <summary>主键段 → 键种类与载荷（键名表 + 单字符 + 内部事件拒绝）。</summary>
    /// <param name="text">原始快捷键串（错误提示定位用）。</param>
    /// <param name="keyName">归一小写主键段。</param>
    /// <param name="ctrl">Ctrl 修饰。</param>
    /// <param name="meta">Meta 修饰。</param>
    /// <param name="shift">Shift 修饰。</param>
    /// <param name="shortcut">输出：解析结果。</param>
    /// <param name="error">输出：失败原因（四要素）。</param>
    /// <returns>解析是否成功。</returns>
    private static bool TryBuild(
        string text,
        string keyName,
        bool ctrl,
        bool meta,
        bool shift,
        ref KeyShortcut shortcut,
        out string error)
    {
        error = string.Empty;
        KeyKind kind;
        switch (keyName)
        {
            case "escape" or "esc":
                kind = KeyKind.Escape;
                break;
            case "enter" or "return":
                kind = KeyKind.Return;
                break;
            case "tab":
                // shift+tab 特例归一：w2 解析器把 Shift+Tab 归一为 BackTab（ESC[Z 恒 Shift=true）
                kind = shift ? KeyKind.BackTab : KeyKind.Tab;
                break;
            case "backtab":
                kind = KeyKind.BackTab;
                break;
            case "backspace":
                kind = KeyKind.Backspace;
                break;
            case "delete":
                kind = KeyKind.Delete;
                break;
            case "up" or "↑":
                kind = KeyKind.UpArrow;
                break;
            case "down" or "↓":
                kind = KeyKind.DownArrow;
                break;
            case "left" or "←":
                kind = KeyKind.LeftArrow;
                break;
            case "right" or "→":
                kind = KeyKind.RightArrow;
                break;
            case "home":
                kind = KeyKind.Home;
                break;
            case "end":
                kind = KeyKind.End;
                break;
            case "pageup":
                kind = KeyKind.PageUp;
                break;
            case "pagedown":
                kind = KeyKind.PageDown;
                break;
            case "space":
                kind = KeyKind.Char;
                break;
            case "f1":
                kind = KeyKind.F1;
                break;
            case "f2":
                kind = KeyKind.F2;
                break;
            case "f3":
                kind = KeyKind.F3;
                break;
            case "f4":
                kind = KeyKind.F4;
                break;
            case "f5":
                kind = KeyKind.F5;
                break;
            case "f6":
                kind = KeyKind.F6;
                break;
            case "f7":
                kind = KeyKind.F7;
                break;
            case "f8":
                kind = KeyKind.F8;
                break;
            case "f9":
                kind = KeyKind.F9;
                break;
            case "f10":
                kind = KeyKind.F10;
                break;
            case "f11":
                kind = KeyKind.F11;
                break;
            case "f12":
                kind = KeyKind.F12;
                break;
            case "paste" or "focusin" or "focusout" or "unknown":
                error = $"键名 \"{keyName}\"（\"{text}\"，KeyShortcut 解析层拦截）：这是内部事件通道（粘贴块/终端焦点/无法识别序列），不是用户可绑定按键。正确写法：换有效键名，如 \"escape\"、\"enter\"";
                return false;
            case "wheelup" or "wheeldown":
                error = $"键名 \"{keyName}\"（\"{text}\"，KeyShortcut 解析层拦截）：鼠标滚轮事件在 w2 键模型中无对应 KeyKind（基准走 ink wheel 通道）。正确写法：换键盘键名，如 \"up\"（翻页语义用 \"pageup\"）";
                return false;
            default:
                if (keyName.Length == 1 && !char.IsControl(keyName[0]))
                {
                    kind = KeyKind.Char;
                    break;
                }

                error = $"未知键名 \"{keyName}\"（\"{text}\"，KeyShortcut 解析层拦截）：不在有效键名表内。有效键名：escape/enter/tab/backtab/backspace/delete/up/down/left/right/home/end/pageup/pagedown/space/f1-f12 或单个字符。正确写法示例：\"ctrl+x\"、\"shift+tab\"、\"f1\"";
                return false;
        }

        var input = kind == KeyKind.Char ? (keyName == "space" ? " " : keyName) : string.Empty;
        // BackTab 恒 Shift=true（w2 归一语义 — 匹配侧 KeyPress BackTab 产出恒带 Shift）
        var normalizedShift = kind == KeyKind.BackTab || shift;
        shortcut = new KeyShortcut(kind, input, ctrl, meta, normalizedShift);
        return true;
    }

    /// <summary>
    /// 精确匹配按键（Kind + 三修饰全等；Char 大小写不敏感 — 对齐基准 match.ts getKeyName 小写化 + modifiersMatch）。
    /// Escape 忽略 Meta（基准 match.ts:96-105 quirk：ESC ESC 序列第二键带 Meta，绑定 "escape" 不带修饰仍命中）。
    /// </summary>
    /// <param name="key">运行时按键事件。</param>
    /// <returns>是否命中。</returns>
    public bool Matches(KeyPress key)
    {
        if (Kind == KeyKind.Char)
        {
            if (key.Kind != KeyKind.Char || Input is not { Length: 1 } || key.Input is not { Length: 1 })
            {
                return false;
            }

            if (char.ToLowerInvariant(key.Input[0]) != Input[0])
            {
                return false;
            }
        }
        else if (key.Kind != Kind)
        {
            return false;
        }

        if (key.Ctrl != Ctrl || key.Shift != Shift)
        {
            return false;
        }

        return Kind == KeyKind.Escape || key.Meta == Meta;
    }

    /// <summary>
    /// 反向序列化（调试/错误提示用，与 <see cref="Parse(string)"/> 往返一致）：
    /// "ctrl+x"、"shift+tab"（BackTab 的 Shift 内化进键名）、"escape"、"alt+enter"、"space"。
    /// </summary>
    /// <returns>规范快捷键串。</returns>
    public override string ToString()
    {
        if (Kind == KeyKind.Char && Input is not { Length: 1 })
        {
            // default(KeyShortcut) 防御（未初始化实例 — 解析路径不会产出）
            return "?";
        }

        var key = Kind switch
        {
            KeyKind.Char => Input == " " ? "space" : Input,
            KeyKind.Escape => "escape",
            KeyKind.Return => "enter",
            KeyKind.Tab => "tab",
            KeyKind.BackTab => Shift ? "shift+tab" : "backtab",
            KeyKind.Backspace => "backspace",
            KeyKind.Delete => "delete",
            KeyKind.UpArrow => "up",
            KeyKind.DownArrow => "down",
            KeyKind.LeftArrow => "left",
            KeyKind.RightArrow => "right",
            KeyKind.Home => "home",
            KeyKind.End => "end",
            KeyKind.PageUp => "pageup",
            KeyKind.PageDown => "pagedown",
            KeyKind.F1 => "f1",
            KeyKind.F2 => "f2",
            KeyKind.F3 => "f3",
            KeyKind.F4 => "f4",
            KeyKind.F5 => "f5",
            KeyKind.F6 => "f6",
            KeyKind.F7 => "f7",
            KeyKind.F8 => "f8",
            KeyKind.F9 => "f9",
            KeyKind.F10 => "f10",
            KeyKind.F11 => "f11",
            KeyKind.F12 => "f12",
            _ => "?",
        };
        if (Kind == KeyKind.BackTab)
        {
            // BackTab 的 Shift 已内化进键名（shift+tab），不再前缀
            return (Ctrl ? "ctrl+" : string.Empty) + (Meta ? "alt+" : string.Empty) + key;
        }

        return (Ctrl ? "ctrl+" : string.Empty) + (Meta ? "alt+" : string.Empty) + (Shift ? "shift+" : string.Empty) + key;
    }
}
