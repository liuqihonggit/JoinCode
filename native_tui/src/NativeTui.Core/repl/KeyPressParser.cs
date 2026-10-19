namespace NativeTui.Core.Repl;

/// <summary>
/// 按键序列解码器 — 对齐基准 ink/parse-keypress + useTextInput 的序列语义。
/// 契约：TryParseNext 把 buffer 视为当前全部可用输入 —
/// chunk 末尾的孤立 ESC 解析为 Escape（基准用 ESC_TIMEOUT flush，此处以 chunk 边界近似）；
/// 不完整前缀（如 "\x1b["）返回 false 且不消耗，由调用方攒够后重试。
/// 无法识别的序列整体吞掉为 Unknown（Input 空），防止转义序列泄漏成文本（对齐基准 suppress 语义）。
/// </summary>
public static class KeyPressParser
{
    /// <summary>从 buffer 头部解析一个按键；成功则消耗并推进 buffer，前缀不完整返回 false。</summary>
    public static bool TryParseNext(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        if (buffer.IsEmpty)
        {
            key = NamedUnknown();
            return false;
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

    /// <summary>CSI 序列：ESC [ params {;mod} final — 方向键/Home/End/Delete/Page + 修饰符 + SGR 鼠标吞噬。</summary>
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

        var numEnd = 0;
        while (numEnd < rest.Length && char.IsDigit(rest[numEnd]))
        {
            numEnd++;
        }

        if (numEnd < rest.Length && rest[numEnd] == ';')
        {
            return ParseCsiWithModifier(ref buffer, rest, numEnd, out key);
        }

        if (numEnd == rest.Length)
        {
            // 数字后无终点符 — 不完整
            key = NamedUnknown();
            return false;
        }

        var final = rest[numEnd];
        var number = numEnd == 0 ? -1 : int.Parse(rest[..numEnd]);
        key = BuildSimple(final, number) ?? NamedUnknown();
        buffer = buffer[(2 + numEnd + 1)..];
        return true;
    }

    /// <summary>带修饰符的 CSI（ESC [ n ; mod final）：mod-1 位掩码 bit0=Shift bit1=Alt bit2=Ctrl。</summary>
    private static bool ParseCsiWithModifier(ref ReadOnlySpan<char> buffer, ReadOnlySpan<char> rest, int numEnd, out KeyPress key)
    {
        var modStart = numEnd + 1;
        var modEnd = modStart;
        while (modEnd < rest.Length && char.IsDigit(rest[modEnd]))
        {
            modEnd++;
        }

        if (modEnd == modStart || modEnd == rest.Length)
        {
            key = NamedUnknown();
            return false;
        }

        var final = rest[modEnd];
        var number = int.Parse(rest[..numEnd]);
        var mod = int.Parse(rest[modStart..modEnd]);
        key = BuildSimple(final, number) ?? NamedUnknown();
        if (key.Kind != KeyKind.Unknown)
        {
            var bits = Math.Max(0, mod - 1);
            key = key with
            {
                Shift = key.Shift || (bits & 1) != 0,
                Meta = key.Meta || (bits & 2) != 0,
                Ctrl = key.Ctrl || (bits & 4) != 0,
            };
        }

        buffer = buffer[(2 + modEnd + 1)..];
        return true;
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

    /// <summary>简单序列 → 按键（final + 可选数字）：字母终点 / ~ 终点数字表。</summary>
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
