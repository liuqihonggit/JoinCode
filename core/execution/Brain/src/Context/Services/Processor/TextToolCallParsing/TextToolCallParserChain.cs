namespace Core.Context;

/// <summary>
/// 文本工具调用解析器责任链 — 依次尝试多个解析器，首个成功即返回。
/// 对应不同 LLM 在 content 里输出的各种工具调用文本格式（DSML/DeepSeek原生/antml/XML/JSON）。
/// 顺序按格式特异性从高到低排列，最特殊的先试，避免误匹配。
/// </summary>
public sealed class TextToolCallParserChain
{
    private readonly IReadOnlyList<ITextToolCallParser> _parsers;

    /// <summary>
    /// 初始化责任链
    /// </summary>
    /// <param name="parsers">按优先级排列的解析器列表</param>
    public TextToolCallParserChain(IEnumerable<ITextToolCallParser> parsers)
    {
        _parsers = parsers as IReadOnlyList<ITextToolCallParser> ?? parsers.ToList();
    }

    /// <summary>
    /// 依次尝试解析器，首个成功即返回；全部失败返回 null
    /// </summary>
    public TextToolCallParseResult? TryParse(ReadOnlySpan<char> content)
    {
        foreach (var parser in _parsers)
        {
            var result = parser.TryParse(content);
            if (result is not null) return result;
        }
        return null;
    }
}
