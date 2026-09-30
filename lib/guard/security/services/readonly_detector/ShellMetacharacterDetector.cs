namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Shell 元字符检测器实现 — 位掩码 O(1) 查找
/// <para>单一数据源: ShellMetaLowMask/HighMask 位掩码常量</para>
/// </summary>
internal sealed class ShellMetacharacterDetector : IShellMetacharacterDetector {
    /// <summary>
    /// Shell 元字符位掩码 — 128 位覆盖 ASCII 0-127，分高低两个 ulong。
    /// 低 64 位 (char 0-63): \n \r ! # $ &amp; ; &lt; &gt;
    /// 高 64 位 (char 64-127): ` \ { | }
    /// </summary>
    private const ulong ShellMetaLowMask =
        (1UL << '\n') | (1UL << '\r') | (1UL << '!') | (1UL << '#') |
        (1UL << '$') | (1UL << '&') | (1UL << ';') | (1UL << '<') | (1UL << '>');
    private const ulong ShellMetaHighMask =
        (1UL << ('`' - 64)) | (1UL << ('\\' - 64)) | (1UL << ('{' - 64)) |
        (1UL << ('|' - 64)) | (1UL << ('}' - 64));

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsShellMetacharacter(char c) {
        if (c < 64) return BitMask.Contains64(ShellMetaLowMask, c);
        if (c < 128) return BitMask.Contains64(ShellMetaHighMask, c - 64);
        return false;
    }

    /// <inheritdoc />
    public bool ContainsShellMetacharacters(string command) {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++) {
            var c = command[i];

            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
            if (inSingleQuote || inDoubleQuote) continue;

            if (IsShellMetacharacter(c)) return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool ContainsShellOperators(string command) {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++) {
            var c = command[i];

            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
            if (inSingleQuote || inDoubleQuote) continue;

            if (c is '|' or ';' or '&' or '<' or '>' or '`') return true;
            if (c == '>' && i + 1 < command.Length && command[i + 1] == '>') return true;
        }

        return false;
    }
}
