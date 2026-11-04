namespace JoinCode.Abstractions.Utils;

/// <summary>
/// ANSI 转义序列清洗器 — 从日志文本中剔除 CSI/OSC/单字节转义，保留纯文本。
/// <para>用于清洗 GitHub Actions 日志中的彩色输出（dotnet test / npm 等含 \x1b[31m 等 SGR 序列），</para>
/// <para>避免 ANSI 噪声污染 LLM 上下文、浪费 token。</para>
/// <para>零 GC: 无 ANSI 时原样返回（同一引用）；有 ANSI 时栈分配 buffer（≤4KB）或堆分配一次。</para>
/// <para>覆盖: CSI(\x1b[...m/H/J/K 等) / OSC(\x1b]...\x07 或 \x1b]...\x1b\\) / 单字节转义(\x1b c)。</para>
/// <para>GAP-038-01: GitHub Actions 彩色日志污染 LLM 上下文</para>
/// </summary>
public static class AnsiStripper {
    private const char Esc = '\x1b';
    private const int StackAllocThreshold = 4096;

    /// <summary>
    /// 清洗 ANSI 转义序列 — string 版本。
    /// <para>无 ANSI 时原样返回（零分配，同一引用）；有 ANSI 时返回新 string。</para>
    /// </summary>
    /// <param name="input">可能含 ANSI 转义序列的输入字符串。</param>
    /// <returns>剔除所有 ANSI 转义序列后的纯文本。</returns>
    public static string Strip(string input) {
        if (string.IsNullOrEmpty(input)) return input;
        var span = input.AsSpan();
        if (span.IndexOf(Esc) < 0) return input;

        if (span.Length <= StackAllocThreshold) {
            Span<char> buffer = stackalloc char[span.Length];
            var written = StripCore(span, buffer);
            return new string(buffer[..written]);
        }
        var heapBuffer = new char[span.Length];
        var heapWritten = StripCore(span, heapBuffer);
        return new string(heapBuffer, 0, heapWritten);
    }

    /// <summary>
    /// 清洗 ANSI 转义序列 — Span 版本，写入 caller 提供的 buffer，返回写入长度。
    /// <para>零 GC: 不分配任何堆内存。buffer 长度须 ≥ input.Length。</para>
    /// </summary>
    /// <param name="input">可能含 ANSI 转义序列的输入 Span。</param>
    /// <param name="output">输出 buffer，长度须 ≥ input.Length。</param>
    /// <returns>写入 output 的字符数。</returns>
    public static int Strip(ReadOnlySpan<char> input, Span<char> output) {
        if (input.IndexOf(Esc) < 0) {
            input.CopyTo(output);
            return input.Length;
        }
        return StripCore(input, output);
    }

    /// <summary>
    /// 检测输入是否含 ANSI 转义序列 — O(n) 扫描，零分配。
    /// </summary>
    public static bool ContainsAnsi(ReadOnlySpan<char> input) => input.IndexOf(Esc) >= 0;

    /// <summary>
    /// 状态机核心 — 逐字符扫描，跳过 ANSI 转义序列，保留纯文本。
    /// <para>CSI: \x1b[ + 参数字节(0x30-0x3F) + 终止字节(0x40-0x7E)</para>
    /// <para>OSC: \x1b] + 内容 + \x07(BEL) 或 \x1b\\(ST)</para>
    /// <para>单字节转义: \x1b X（X 为任意单字符）</para>
    /// </summary>
    private static int StripCore(ReadOnlySpan<char> input, Span<char> output) {
        var written = 0;
        var i = 0;
        var len = input.Length;
        while (i < len) {
            if (input[i] != Esc) {
                output[written++] = input[i++];
                continue;
            }
            i++;
            if (i >= len) break;
            var kind = input[i];
            if (kind == '[') {
                i++;
                while (i < len && input[i] >= 0x30 && input[i] <= 0x3F) i++;
                if (i < len && input[i] >= 0x40 && input[i] <= 0x7E) i++;
            } else if (kind == ']') {
                i++;
                while (i < len) {
                    if (input[i] == '\x07') { i++; break; }
                    if (input[i] == Esc && i + 1 < len && input[i + 1] == '\\') { i += 2; break; }
                    i++;
                }
            } else {
                i++;
            }
        }
        return written;
    }
}
