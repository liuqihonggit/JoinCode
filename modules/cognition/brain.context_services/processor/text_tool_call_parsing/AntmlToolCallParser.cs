// JCC1017 抑制: 存量手写 JSON, 后续改为 DTO+JsonContext
#pragma warning disable JCC1017
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
            var arguments = ParseAntmlArguments(invokeBody);
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
            var arguments = ParseArguments(inputBody);
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
                var arguments = ParseArguments(invokeBody);
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

    private static string ParseAntmlArguments(string invokeBody) {
        var paramMatches = AntmlParameterRegex.Matches(invokeBody);
        if (paramMatches.Count == 0) return "{}";

        var sb = new StringBuilder("{");
        for (var i = 0; i < paramMatches.Count; i++) {
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

    private static string ParseArguments(string inputBody) {
        var paramMatches = ParameterRegex.Matches(inputBody);
        if (paramMatches.Count == 0) return "{}";

        var sb = new StringBuilder("{");
        for (var i = 0; i < paramMatches.Count; i++) {
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
