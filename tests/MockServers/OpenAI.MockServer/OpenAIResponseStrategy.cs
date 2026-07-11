namespace OpenAI.MockServer;

/// <summary>
/// OpenAI 脚本化响应策略 — 支持预设对话返回和工具调用
/// </summary>
public sealed class OpenAIResponseStrategy : ScriptedResponseStrategyBase
{
    public OpenAIResponseStrategy(List<ScriptedTurn>? turns, string defaultResponse)
        : base(turns, defaultResponse) { }

    public override string BuildResponse(JsonElement request, CacheStats cacheStats)
    {
        var turn = CurrentTurn;
        var promptTokens = cacheStats.InputTokens;

        return $$"""
        {
            "id": "chatcmpl-{{Guid.NewGuid():N}}",
            "object": "chat.completion",
            "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
            "model": "gpt-4o",
            "choices": [{
                "index": 0,
                "message": {"role": "assistant", "content": "{{EscapeJsonString(turn.TextResponse ?? DefaultResponse)}}"},
                "finish_reason": "stop"
            }],
            "usage": {
                "prompt_tokens": {{promptTokens}},
                "completion_tokens": {{cacheStats.OutputTokens}},
                "total_tokens": {{promptTokens + cacheStats.OutputTokens}},
                "prompt_tokens_details": {
                    "cached_tokens": {{cacheStats.CacheReadTokens}}
                }
            }
        }
        """;
    }

    public override string BuildStreamChunk(string id, string content, bool isLast)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(content);
        if (isLast)
        {
            return $"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{}},\"finish_reason\":\"stop\"}}]}}\n\ndata: [DONE]\n\n";
        }

        return $"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"content\":\"{EscapeJsonString(content)}\"}},\"finish_reason\":null}}]}}\n\n";
    }

    /// <summary>
    /// 流式最终 chunk — 真实 OpenAI API 在 stream_options.include_usage=true 时,
    /// 最后一个 chunk 包含 usage 字段(含 prompt_tokens_details.cached_tokens)。
    /// </summary>
    public override string BuildStreamFinalChunk(string id, CacheStats cacheStats)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(cacheStats);
        var promptTokens = cacheStats.InputTokens;

        return $"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{}},\"finish_reason\":\"stop\"}}],\"usage\":{{\"prompt_tokens\":{promptTokens},\"completion_tokens\":{cacheStats.OutputTokens},\"total_tokens\":{promptTokens + cacheStats.OutputTokens},\"prompt_tokens_details\":{{\"cached_tokens\":{cacheStats.CacheReadTokens}}}}}}}\n\ndata: [DONE]\n\n";
    }

    public override string? BuildStreamPreamble(string id) => null;

    public override string BuildToolCallResponse(JsonElement request, CacheStats cacheStats)
    {
        var turn = CurrentTurn;
        var toolCalls = turn.ToolCalls ?? [];
        var toolCallsJson = BuildToolCallsJson(toolCalls);

        return $$"""
        {
            "id": "chatcmpl-{{Guid.NewGuid():N}}",
            "object": "chat.completion",
            "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
            "model": "gpt-4o",
            "choices": [{
                "index": 0,
                "message": {"role": "assistant", "content": null, "tool_calls": [{{toolCallsJson}}]},
                "finish_reason": "tool_calls"
            }],
            "usage": {
                "prompt_tokens": {{cacheStats.InputTokens}},
                "completion_tokens": {{cacheStats.OutputTokens}},
                "total_tokens": {{cacheStats.InputTokens + cacheStats.OutputTokens}}
            }
        }
        """;
    }

    public override string BuildStreamToolCallResponse(string id)
    {
        var turn = CurrentTurn;
        var toolCalls = turn.ToolCalls ?? [];
        var sb = new StringBuilder();

        // 第一个分片: 角色和工具调用开始
        var first = toolCalls[0];
        var firstId = GenerateToolCallId(first);
        sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"role\":\"assistant\",\"tool_calls\":[{{\"index\":0,\"id\":\"{firstId}\",\"type\":\"function\",\"function\":{{\"name\":\"{first.ToolName}\",\"arguments\":\"\"}}}}]}},\"finish_reason\":null}}]}}\n\n");

        // 工具参数分片
        sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"tool_calls\":[{{\"index\":0,\"function\":{{\"arguments\":\"{EscapeJsonString(first.Arguments)}\"}}}}]}},\"finish_reason\":null}}]}}\n\n");

        // 多工具调用: 后续工具
        for (var i = 1; i < toolCalls.Count; i++)
        {
            var tc = toolCalls[i];
            var tcId = GenerateToolCallId(tc);
            sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"tool_calls\":[{{\"index\":{i},\"id\":\"{tcId}\",\"type\":\"function\",\"function\":{{\"name\":\"{tc.ToolName}\",\"arguments\":\"\"}}}}]}},\"finish_reason\":null}}]}}\n\n");
            sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"tool_calls\":[{{\"index\":{i},\"function\":{{\"arguments\":\"{EscapeJsonString(tc.Arguments)}\"}}}}]}},\"finish_reason\":null}}]}}\n\n");
        }

        // 结束分片
        sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{}},\"finish_reason\":\"tool_calls\"}}]}}\n\ndata: [DONE]\n\n");
        return sb.ToString();
    }

    public override string BuildStreamThinkingResponse(string id)
    {
        // OpenAI 不支持思考内容，返回空
        return "";
    }

    private static string BuildToolCallsJson(List<ToolCallConfig> toolCalls)
    {
        var parts = new List<string>();
        for (var i = 0; i < toolCalls.Count; i++)
        {
            var tc = toolCalls[i];
            var tcId = !string.IsNullOrEmpty(tc.ToolCallId) ? tc.ToolCallId : $"call_{Guid.NewGuid():N}";
            parts.Add($"{{\"id\":\"{tcId}\",\"type\":\"function\",\"function\":{{\"name\":\"{tc.ToolName}\",\"arguments\":\"{EscapeJsonString(tc.Arguments)}\"}}}}");
        }
        return string.Join(",", parts);
    }

    /// <summary>
    /// 转义 JSON 字符串中的特殊字符
    /// </summary>
    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
