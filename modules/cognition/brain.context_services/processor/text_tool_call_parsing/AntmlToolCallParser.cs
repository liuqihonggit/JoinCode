namespace Core.Context;

/// <summary>
/// XML 格式工具调用解析器 — 解析 LLM 在 content 里输出的标准 XML 格式工具调用。
/// 支持 antml:invoke、tool_use、function_calls 三种格式。
/// 此格式为某些模型不支持 function calling 协议时在 content 里的 fallback 输出，
/// 或模型异常输出时（如 CodeArts AI 输出 &lt;parameter name="newString"&gt;0）的防御性解析。
/// </summary>
[Register(typeof(ITextToolCallParser), ServiceLifetime.Singleton)]
public sealed class AntmlToolCallParser : ITextToolCallParser {
    private static readonly Regex AntmlInvokeRegex = new(
        @"<antml:invoke\s+name=""([^""]+)"">\s*(.*?)\s*</antml:invoke>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AntmlParameterRegex = new(
        @"<antml:parameter\s+name=""([^""]+)"">(.*?)</antml:parameter>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ToolUseRegex = new(
        @"<tool_use>\s*<name>([^<]+)</name>\s*<input>\s*(.*?)\s*</input>\s*</tool_use>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex FunctionCallsRegex = new(
        @"<function_calls>\s*(.*?)\s*</function_calls>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex InvokeRegex = new(
        @"<invoke\s+name=""([^""]+)"">\s*(.*?)\s*</invoke>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ParameterRegex = new(
        @"<parameter\s+name=""([^""]+)"">(.*?)</parameter>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <inheritdoc />
    public TextToolCallParseResult? TryParse(ReadOnlySpan<char> content) {
        if (content.IsEmpty) return null;

        var contentStr = content.ToString();

        var result = TryParseAntmlInvoke(contentStr);
        if (result is not null) return result;

        result = TryParseToolUse(contentStr);
        if (result is not null) return result;

        result = TryParseFunctionCalls(contentStr);
        if (result is not null) return result;

        return null;
    }

    private static TextToolCallParseResult? TryParseAntmlInvoke(string content) {
        var matches = AntmlInvokeRegex.Matches(content);
        if (matches.Count == 0) return null;

        var toolCalls = new List<ToolCallEntry>();
        foreach (Match m in matches) {
            var toolName = m.Groups[1].Value;
            var invokeBody = m.Groups[2].Value;
            var arguments = ExtractParametersToJson(invokeBody, AntmlParameterRegex);
            toolCalls.Add(new ToolCallEntry {
                Id = null,
                Name = toolName,
                Arguments = arguments
            });
        }

        if (toolCalls.Count == 0) return null;

        var remainingText = AntmlInvokeRegex.Replace(content, "").Trim();
        if (string.IsNullOrEmpty(remainingText)) remainingText = null;

        return new TextToolCallParseResult {
            ToolCalls = toolCalls,
            RemainingText = remainingText
        };
    }

    private static TextToolCallParseResult? TryParseToolUse(string content) {
        var matches = ToolUseRegex.Matches(content);
        if (matches.Count == 0) return null;

        var toolCalls = new List<ToolCallEntry>();
        foreach (Match m in matches) {
            var toolName = m.Groups[1].Value.Trim();
            var inputBody = m.Groups[2].Value;
            var arguments = ExtractParametersToJson(inputBody, ParameterRegex);
            toolCalls.Add(new ToolCallEntry {
                Id = null,
                Name = toolName,
                Arguments = arguments
            });
        }

        if (toolCalls.Count == 0) return null;

        var remainingText = ToolUseRegex.Replace(content, "").Trim();
        if (string.IsNullOrEmpty(remainingText)) remainingText = null;

        return new TextToolCallParseResult {
            ToolCalls = toolCalls,
            RemainingText = remainingText
        };
    }

    private static TextToolCallParseResult? TryParseFunctionCalls(string content) {
        var fcMatches = FunctionCallsRegex.Matches(content);
        if (fcMatches.Count == 0) return null;

        var toolCalls = new List<ToolCallEntry>();
        foreach (Match fcMatch in fcMatches) {
            var fcBody = fcMatch.Groups[1].Value;
            var invokeMatches = InvokeRegex.Matches(fcBody);
            foreach (Match m in invokeMatches) {
                var toolName = m.Groups[1].Value;
                var invokeBody = m.Groups[2].Value;
                var arguments = ExtractParametersToJson(invokeBody, ParameterRegex);
                toolCalls.Add(new ToolCallEntry {
                    Id = null,
                    Name = toolName,
                    Arguments = arguments
                });
            }
        }

        if (toolCalls.Count == 0) return null;

        var remainingText = FunctionCallsRegex.Replace(content, "").Trim();
        if (string.IsNullOrEmpty(remainingText)) remainingText = null;

        return new TextToolCallParseResult {
            ToolCalls = toolCalls,
            RemainingText = remainingText
        };
    }

    /// <summary>
    /// 从 XML 参数标签中提取键值对，序列化为 JSON 字符串。
    /// 使用 JsonSerializer.Serialize 替代手写 StringBuilder 拼接，确保 JSON 格式正确性。
    /// </summary>
    private static string ExtractParametersToJson(string body, Regex parameterRegex) {
        var paramMatches = parameterRegex.Matches(body);
        if (paramMatches.Count == 0) return "{}";

        var dict = new Dictionary<string, string>(paramMatches.Count);
        foreach (Match m in paramMatches)
            dict[m.Groups[1].Value] = m.Groups[2].Value;

        return JsonSerializer.Serialize(dict, ChatServiceJsonContext.Default.DictionaryStringString);
    }
}
