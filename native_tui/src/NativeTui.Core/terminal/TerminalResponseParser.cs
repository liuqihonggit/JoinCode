namespace NativeTui.Core.Terminal;

/// <summary>
/// 终端查询响应解析器 — 基准 ink/parse-keypress.ts:96-175 parseTerminalResponse：
/// 程序主动发的终端探测查询（DECRQM/DA1/DA2/kitty flags/DSR/XTVERSION/OSC），其回复经 stdin
/// 回流被识别为 TerminalResponse（非键事件，路由给等待方 promise）。
/// 契约：TryParse 只识别完整回复形态 — 非回复输入（普通键序列/文本）返回 null 且不消耗
///（调用方继续走键解析）；不完整前缀返回 null（跨 chunk 攒齐由调用方持有重试）。
/// </summary>
public static class TerminalResponseParser
{
    /// <summary>从 buffer 头部尝试解析一个终端响应；命中返回响应并给出消耗长度，非回复返回 null。</summary>
    public static TerminalResponse? TryParse(ReadOnlySpan<char> buffer, out int consumed)
    {
        consumed = 0;
        if (buffer.Length < 2 || buffer[0] != '\u001b')
        {
            return null;
        }

        return buffer[1] switch
        {
            ']' => TryParseOsc(buffer, out consumed),
            '[' => TryParseCsiReply(buffer, out consumed),
            _ => null,
        };
    }

    /// <summary>CSI 形态回复：ESC [ {?|&gt;} params {intermediate} final — DECRPM/DA1/DA2/kitty flags/DSR。</summary>
    private static TerminalResponse? TryParseCsiReply(ReadOnlySpan<char> buffer, out int consumed)
    {
        consumed = 0;
        var rest = buffer[2..];
        if (rest.IsEmpty)
        {
            return null;
        }

        var prefix = '\0';
        var start = 0;
        if (rest[0] is '?' or '>')
        {
            prefix = rest[0];
            start = 1;
        }

        var scan = start;
        while (scan < rest.Length && IsParameterRegion(rest[scan]))
        {
            scan++;
        }

        if (scan == rest.Length)
        {
            return null;
        }

        var final = rest[scan];
        var parameters = rest[start..scan];
        var numbers = CollectNumbers(parameters);
        var raw = buffer[..(2 + scan + 1)].ToString();

        var kind = (prefix, final, hasDollar: parameters.Contains('$'), hasSemicolon: parameters.Contains(';')) switch
        {
            ('?', 'y', true, _) => TerminalResponseKind.Decrpm,
            ('?', 'u', _, _) => TerminalResponseKind.KittyFlags,
            ('?', 'c', _, _) => TerminalResponseKind.Da1,
            ('>', 'c', _, _) => TerminalResponseKind.Da2,
            ('\0', 'R', _, true) => TerminalResponseKind.CursorPosition,
            _ => (TerminalResponseKind?)null,
        };

        if (kind is null)
        {
            return null;
        }

        consumed = 2 + scan + 1;
        return new TerminalResponse(kind.Value, raw, numbers, Payload: null);
    }

    /// <summary>OSC 形态回复：ESC ] payload 终结（BEL 或 ST）— XTVERSION（0; 前缀）/通用 OSC。</summary>
    private static TerminalResponse? TryParseOsc(ReadOnlySpan<char> buffer, out int consumed)
    {
        consumed = 0;
        for (var i = 2; i < buffer.Length; i++)
        {
            var terminatorLength = buffer[i] switch
            {
                '\u0007' => 1,
                '\u001b' when i + 1 < buffer.Length && buffer[i + 1] == '\\' => 2,
                _ => 0,
            };

            if (terminatorLength == 0)
            {
                continue;
            }

            var payload = buffer[2..i].ToString();
            consumed = i + terminatorLength;
            var raw = buffer[..consumed].ToString();
            var kind = payload.StartsWith("0;", StringComparison.Ordinal)
                ? TerminalResponseKind.XtVersion
                : TerminalResponseKind.Osc;
            // XTVERSION 剥 OSC 0; 前缀 — Payload 只携带厂商身份串（基准 isXtversionName 消费形态）
            var text = kind == TerminalResponseKind.XtVersion && payload.Length > 2 ? payload[2..] : payload;
            return new TerminalResponse(kind, raw, Numbers: [], Payload: text);
        }

        return null;
    }

    /// <summary>CSI 参数区字节（数字 / 分号 / intermediate 0x20-0x2F — DECRPM 的 $ 归此类）。</summary>
    private static bool IsParameterRegion(char ch) =>
        char.IsDigit(ch) || ch == ';' || (ch >= '\u0020' && ch <= '\u002f');

    /// <summary>收集参数区数字段（分号/intermediate 分段；空段计 0 — xterm 空参数语义；
    /// 尾段仅当有数字才收 — DECRPM 的 $ intermediate 尾不产生空段）。</summary>
    private static int[] CollectNumbers(ReadOnlySpan<char> segment)
    {
        var numbers = new List<int>();
        var current = 0;
        var hasDigits = false;
        foreach (var ch in segment)
        {
            if (char.IsDigit(ch))
            {
                current = current * 10 + (ch - '0');
                hasDigits = true;
            }
            else if (IsParameterRegion(ch))
            {
                numbers.Add(hasDigits ? current : 0);
                current = 0;
                hasDigits = false;
            }
        }

        if (hasDigits)
        {
            numbers.Add(current);
        }

        return [.. numbers];
    }
}
