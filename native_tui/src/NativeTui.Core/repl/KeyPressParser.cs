namespace NativeTui.Core.Repl;

/// <summary>
/// 按键序列解码器 — 对齐基准 ink/parse-keypress + useTextInput 的序列语义。
/// 契约：TryParseNext 把 buffer 视为当前全部可用输入 —
/// chunk 末尾的孤立 ESC 解析为 Escape（基准用 ESC_TIMEOUT flush，此处以 chunk 边界近似）；
/// 不完整前缀（如 "\x1b["）返回 false 且不消耗，由调用方攒够后重试。
/// 无法识别的序列整体吞掉为 Unknown（Input 空），防止转义序列泄漏成文本（对齐基准 suppress 语义）。
/// bracketed paste：\x1b[200~ 内容 \x1b[201~ 整块产出 Paste 事件（Input=粘贴原文），
/// 终止标记未到达视为不完整前缀不消耗（粘贴块可跨 chunk 攒齐）。
/// </summary>
public static class KeyPressParser
{
    /// <summary>bracketed paste 起始标记（DEC 2004 模式开启后终端包裹粘贴内容）。</summary>
    private const string PasteStart = "\u001b[200~";

    /// <summary>bracketed paste 终止标记。</summary>
    private const string PasteEnd = "\u001b[201~";

    /// <summary>从 buffer 头部解析一个按键；成功则消耗并推进 buffer，前缀不完整返回 false。</summary>
    public static bool TryParseNext(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        if (buffer.IsEmpty)
        {
            key = NamedUnknown();
            return false;
        }

        if (buffer.StartsWith(PasteStart))
        {
            return ParseBracketedPaste(ref buffer, out key);
        }

        var first = buffer[0];
        switch (first)
        {
            case '\r':
            case '\n':
                key = KeyPress.Named(KeyKind.Return);
                buffer = buffer[1..];
                return true;
            case '\t':
                key = KeyPress.Named(KeyKind.Tab);
                buffer = buffer[1..];
                return true;
            case '\u007f':
            case '\u0008':
                key = KeyPress.Named(KeyKind.Backspace);
                buffer = buffer[1..];
                return true;
            case '\u001f':
                // Ctrl+_（= ctrl+shift+- 同码）— 基准 chat:undo 键位，归一为 Ctrl+"_" 名
                key = new KeyPress(KeyKind.Char, "_", Ctrl: true);
                buffer = buffer[1..];
                return true;
            case '\u001b':
                return ParseEscape(ref buffer, out key);
        }

        if (first < ' ')
        {
            // \x01-\x1a 中的控制字母 → Ctrl+字母（\b \t \n \r 已在上方捕获）
            if (first is >= '\u0001' and <= '\u001a')
            {
                var letter = (char)('a' + first - 1);
                key = new KeyPress(KeyKind.Char, letter.ToString(), Ctrl: true);
            }
            else
            {
                key = NamedUnknown();
            }

            buffer = buffer[1..];
            return true;
        }

        key = KeyPress.Char(first.ToString());
        buffer = buffer[1..];
        return true;
    }

    /// <summary>bracketed paste 块：\x1b[200~ 内容 \x1b[201~ — 整块消费产出 Paste 事件；终止标记缺失返回 false 不消耗（跨 chunk 攒齐）。</summary>
    private static bool ParseBracketedPaste(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        var content = buffer[PasteStart.Length..];
        var endOffset = content.IndexOf(PasteEnd);
        if (endOffset < 0)
        {
            key = NamedUnknown();
            return false;
        }

        key = new KeyPress(KeyKind.Paste, content[..endOffset].ToString());
        buffer = content[(endOffset + PasteEnd.Length)..];
        return true;
    }

    /// <summary>ESC 前缀：CSI（[）/ SS3（O）/ Meta 后缀 / 孤立 Esc。</summary>
    private static bool ParseEscape(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        if (buffer.Length == 1)
        {
            key = KeyPress.Named(KeyKind.Escape);
            buffer = buffer[1..];
            return true;
        }

        var second = buffer[1];
        return second switch
        {
            '[' => ParseCsi(ref buffer, out key),
            'O' => ParseSs3(ref buffer, out key),
            '\u001b' => Consume(ref buffer, 2, KeyPress.Named(KeyKind.Escape, meta: true), out key),
            '\r' or '\n' => Consume(ref buffer, 2, KeyPress.Named(KeyKind.Return, meta: true), out key),
            '\t' => Consume(ref buffer, 2, KeyPress.Named(KeyKind.Tab, meta: true), out key),
            '\u007f' => Consume(ref buffer, 2, KeyPress.Named(KeyKind.Backspace, meta: true), out key),
            >= ' ' => Consume(ref buffer, 2, KeyPress.Char(second.ToString()) with { Meta = true }, out key),
            _ => Consume(ref buffer, 2, NamedUnknown(), out key),
        };
    }

    /// <summary>CSI 序列：ESC [ params {;mod} final — 方向键/Home/End/Delete/Page + 修饰符 + SGR 鼠标吞噬。
    /// 序列边界 = 参数区（0x30-0x3F 数字/;/…）+ intermediate（0x20-0x2F）扫描至 final（0x40-0x7E）；
    /// 非「数字{;数字}」两段形态（私有参数 ESC[?…、intermediate ESC[…$…、三段 modifyOtherKeys、超长数字）
    /// 整体吞 Unknown（防泄漏契约）；数字解析全部 TryParse — 空第一段按 xterm 默认 1、
    /// 超长数字 -1 无匹配（对齐基准 parseInt 永不抛，外部畸形输入不可击穿读线程）。</summary>
    private static bool ParseCsi(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        var rest = buffer[2..];
        if (rest.IsEmpty)
        {
            key = NamedUnknown();
            return false;
        }

        // SGR 鼠标（ESC[&lt;b;x;yM/m）整体吞掉 — 对齐基准防泄漏守卫
        if (rest[0] == '<')
        {
            return ParseSgrMouse(ref buffer, out key);
        }

        var scan = rest.IndexOfAnyExceptInRange('\u0020', '\u003f');
        if (scan < 0)
        {
            scan = rest.Length;
        }

        if (scan == rest.Length)
        {
            // 参数区未闭合（final 未到）— 不完整前缀，等待更多输入
            key = NamedUnknown();
            return false;
        }

        key = ParseTwoSegmentForm(rest, scan, rest[scan]) ?? NamedUnknown();
        buffer = buffer[(2 + scan + 1)..];
        return true;
    }

    /// <summary>「数字{;数字}」两段形态语义解析：单段=简单键、双段=修饰键；空第一段=xterm 默认 1；畸形/超长=null（整体吞 Unknown）。</summary>
    private static KeyPress? ParseTwoSegmentForm(ReadOnlySpan<char> rest, int scan, char final)
    {
        var numEnd = 0;
        while (numEnd < scan && char.IsDigit(rest[numEnd]))
        {
            numEnd++;
        }

        if (numEnd == scan)
        {
            var number = numEnd == 0 ? 1 : int.TryParse(rest[..numEnd], out var plain) ? plain : -1;
            return BuildSimple(final, number);
        }

        if (rest[numEnd] != ';')
        {
            return null;
        }

        var modStart = numEnd + 1;
        var modEnd = modStart;
        while (modEnd < scan && char.IsDigit(rest[modEnd]))
        {
            modEnd++;
        }

        if (modEnd != scan || modEnd == modStart)
        {
            return null;
        }

        var number2 = int.TryParse(rest[..numEnd], out var head) ? head : -1;
        if (!int.TryParse(rest[modStart..modEnd], out var mod))
        {
            return null;
        }

        var simple = BuildSimple(final, number2);
        if (simple is null)
        {
            return null;
        }

        var bits = Math.Max(0, mod - 1);
        return simple with
        {
            Shift = simple.Shift || (bits & 1) != 0,
            Meta = simple.Meta || (bits & 2) != 0,
            Ctrl = simple.Ctrl || (bits & 4) != 0,
        };
    }

    /// <summary>SS3 应用模式（ESC O final）：方向键/Home/End，其余单字符吞掉。</summary>
    private static bool ParseSs3(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        if (buffer.Length == 2)
        {
            key = NamedUnknown();
            return false;
        }

        var final = buffer[2];
        key = final switch
        {
            'A' => KeyPress.Named(KeyKind.UpArrow),
            'B' => KeyPress.Named(KeyKind.DownArrow),
            'C' => KeyPress.Named(KeyKind.RightArrow),
            'D' => KeyPress.Named(KeyKind.LeftArrow),
            'H' => KeyPress.Named(KeyKind.Home),
            'F' => KeyPress.Named(KeyKind.End),
            _ => NamedUnknown(),
        };
        buffer = buffer[3..];
        return true;
    }

    /// <summary>SGR 鼠标序列吞噬：&lt;b;x;y[Mm]，不完整等待更多，非法则吞到扫描位。</summary>
    private static bool ParseSgrMouse(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        var rest = buffer[3..];
        var i = 0;
        var semicolons = 0;
        while (i < rest.Length && (char.IsDigit(rest[i]) || rest[i] == ';'))
        {
            if (rest[i] == ';')
            {
                semicolons++;
            }

            i++;
        }

        if (i == rest.Length)
        {
            key = NamedUnknown();
            return false;
        }

        var consumed = 3 + i + (rest[i] is 'M' or 'm' && semicolons == 2 ? 1 : 0);
        key = NamedUnknown();
        buffer = buffer[consumed..];
        return true;
    }

    /// <summary>简单序列 → 按键（final + 可选数字）：字母终点 / ~ 终点数字表 /
    /// u 终点 kitty 键位码（CSI codepoint;mods u — Windows Terminal 等 kitty 协议终端的
    /// ctrl+shift+字母唯一可区分形态：标准 xterm 下该组合与 ctrl+字母同码不可区分）。</summary>
    private static KeyPress? BuildSimple(char final, int number) =>
        final switch
        {
            'A' => KeyPress.Named(KeyKind.UpArrow),
            'B' => KeyPress.Named(KeyKind.DownArrow),
            'C' => KeyPress.Named(KeyKind.RightArrow),
            'D' => KeyPress.Named(KeyKind.LeftArrow),
            'H' => KeyPress.Named(KeyKind.Home),
            'F' => KeyPress.Named(KeyKind.End),
            'Z' => KeyPress.Named(KeyKind.BackTab, shift: true),
            '~' when number == 1 => KeyPress.Named(KeyKind.Home),
            '~' when number == 3 => KeyPress.Named(KeyKind.Delete),
            '~' when number == 4 => KeyPress.Named(KeyKind.End),
            '~' when number == 5 => KeyPress.Named(KeyKind.PageUp),
            '~' when number == 6 => KeyPress.Named(KeyKind.PageDown),
            'u' when number is > 0 and not (>= 0xD800 and <= 0xDFFF) =>
                // kitty 码点为无修饰形态（小写字母），Shift 由修饰位表达 — codepoint 转字符
                new KeyPress(KeyKind.Char, char.ConvertFromUtf32(number)),
            _ => null,
        };

    /// <summary>消耗前 n 个字符并返回指定按键。</summary>
    private static bool Consume(ref ReadOnlySpan<char> buffer, int count, KeyPress key, out KeyPress result)
    {
        result = key;
        buffer = buffer[Math.Min(count, buffer.Length)..];
        return true;
    }

    private static KeyPress NamedUnknown() => KeyPress.Named(KeyKind.Unknown);
}
