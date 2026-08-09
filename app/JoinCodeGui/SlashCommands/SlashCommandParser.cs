namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 斜杠命令光标解析结果 — 描述当前光标位置是否触发补全及查询前缀。
/// </summary>
public readonly record struct SlashParseResult
{
    /// <summary>是否触发斜杠补全</summary>
    public bool ShouldComplete { get; init; }

    /// <summary>查询前缀（含 /，如 "/ap"）；不触发时为空字符串</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>/ 字符在文本中的索引（回填替换用）；不触发时为 -1</summary>
    public int SlashIndex { get; init; } = -1;

    /// <summary>前缀结束位置（即光标位置，回填替换区间右边界）；不触发时为 -1</summary>
    public int PrefixEnd { get; init; } = -1;

    /// <summary>不触发的空结果</summary>
    public static SlashParseResult None => new();

    public SlashParseResult() { }
}

/// <summary>
/// 斜杠命令光标解析器 — 从光标位置向前查找最近的 /，提取查询前缀。
/// 规则：取光标前最近一个 /，/ 之后到第一个空格前的内容作为前缀；
/// 出现空格立刻终止补全；多行文本任意行均可触发；连续 // 取最后一个 /。
/// </summary>
public static class SlashCommandParser
{
    /// <summary>
    /// 解析文本中光标位置的斜杠命令补全上下文。
    /// </summary>
    /// <param name="text">输入框完整文本</param>
    /// <param name="cursor">光标位置（0 到 text.Length）</param>
    /// <returns>解析结果；不触发时 ShouldComplete=false</returns>
    public static SlashParseResult Parse(string text, int cursor)
    {
        if (string.IsNullOrEmpty(text) || cursor <= 0 || cursor > text.Length)
            return SlashParseResult.None;

        var slice = text.AsSpan(0, cursor);

        var lineStart = slice.LastIndexOf('\n') + 1;
        var lineSlice = slice[lineStart..];
        var slashInLine = lineSlice.LastIndexOf('/');
        if (slashInLine < 0)
            return SlashParseResult.None;

        var slashIndex = lineStart + slashInLine;
        var afterSlash = slice[(slashIndex + 1)..];
        if (afterSlash.IndexOf(' ') >= 0)
            return SlashParseResult.None;

        var prefix = slice[slashIndex..].ToString();
        return new SlashParseResult
        {
            ShouldComplete = true,
            Prefix = prefix,
            SlashIndex = slashIndex,
            PrefixEnd = cursor
        };
    }
}
