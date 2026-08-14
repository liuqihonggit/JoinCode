namespace Core.Context;

/// <summary>
/// DSML 格式工具调用解析器 — 解析 LLM 在 content 里输出的 &lt;｜DSML｜tool_calls&gt; 文本块。
/// 全角竖线 ｜ = U+FF5C。此格式为某些模型不支持 function calling 协议时在 content 里的 fallback 输出。
/// 格式样本：
///   &lt;｜DSML｜tool_calls&gt;
///     &lt;｜DSML｜invoke name="Bash"&gt;
///       &lt;｜DSML｜parameter name="command" string="true"&gt;ls -la&lt;/｜DSML｜parameter&gt;
///     &lt;/｜DSML｜invoke&gt;
///   &lt;/｜DSML｜tool_calls&gt;
/// </summary>
public sealed class DsmlToolCallParser : ITextToolCallParser
{
    private const string ToolCallsOpen = "<\uFF5CDSML\uFF5Ctool_calls>";
    private const string ToolCallsClose = "</\uFF5CDSML\uFF5Ctool_calls>";

    private static readonly Regex InvokeRegex = new(
        @"<\uFF5CDSML\uFF5Cinvoke\s+name=""([^""]+)"">\s*(.*?)\s*</\uFF5CDSML\uFF5Cinvoke>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ParameterRegex = new(
        @"<\uFF5CDSML\uFF5Cparameter\s+name=""([^""]+)""[^>]*>(.*?)</\uFF5CDSML\uFF5Cparameter>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <inheritdoc />
    public TextToolCallParseResult? TryParse(ReadOnlySpan<char> content)
    {
        if (content.IsEmpty) return null;

        var text = content.ToString();
        var startIdx = text.IndexOf(ToolCallsOpen, StringComparison.Ordinal);
        if (startIdx < 0) return null;

        var endIdx = text.IndexOf(ToolCallsClose, startIdx, StringComparison.Ordinal);
        if (endIdx < 0) return null;

        var blockStart = startIdx + ToolCallsOpen.Length;
        var block = text.Substring(blockStart, endIdx - blockStart);

        var toolCalls = new List<ToolCallEntry>();
        var matches = InvokeRegex.Matches(block);
        foreach (Match m in matches)
        {
            var toolName = m.Groups[1].Value;
            var invokeBody = m.Groups[2].Value;
            var arguments = ParseArguments(invokeBody);
            toolCalls.Add(new ToolCallEntry
            {
                Id = null,
                Name = toolName,
                Arguments = arguments
            });
        }

        if (toolCalls.Count == 0) return null;

        string? remaining = null;
        var before = text.AsSpan(0, startIdx);
        var after = text.AsSpan(endIdx + ToolCallsClose.Length);
        if (!before.IsEmpty || !after.IsEmpty)
        {
            var sb = new StringBuilder(before.Length + after.Length);
            sb.Append(before);
            sb.Append(after);
            remaining = sb.ToString().Trim();
            if (remaining.Length == 0) remaining = null;
        }

        return new TextToolCallParseResult
        {
            ToolCalls = toolCalls,
            RemainingText = remaining
        };
    }

    private static string ParseArguments(string invokeBody)
    {
        var paramMatches = ParameterRegex.Matches(invokeBody);
        if (paramMatches.Count == 0) return "{}";

        var sb = new StringBuilder("{");
        for (var i = 0; i < paramMatches.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var name = paramMatches[i].Groups[1].Value;
            var value = paramMatches[i].Groups[2].Value;
            sb.Append('"');
            sb.Append(JsonEncodedText.Encode(name).Value);
            sb.Append("\":\"");
            sb.Append(JsonEncodedText.Encode(value).Value);
            sb.Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }
}
