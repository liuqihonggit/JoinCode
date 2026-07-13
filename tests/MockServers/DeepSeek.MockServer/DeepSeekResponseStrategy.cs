namespace DeepSeek.MockServer;

/// <summary>
/// DeepSeek 脚本化响应策略 — 支持预设对话返回、工具调用和思考内容
/// </summary>
public sealed class DeepSeekResponseStrategy : ScriptedResponseStrategyBase
{
    public DeepSeekResponseStrategy(List<ScriptedTurn>? turns, string defaultResponse)
        : base(turns, defaultResponse) { }

    public override string BuildResponse(JsonElement request, CacheStats cacheStats)
    {
        var turn = CurrentTurn;
        var cacheHitTokens = cacheStats.CacheReadTokens;
        var cacheMissTokens = cacheStats.CacheCreationTokens;

        return $$"""
        {
            "id": "chatcmpl-{{Guid.NewGuid():N}}",
            "object": "chat.completion",
            "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
            "model": "deepseek-v4-flash",
            "choices": [{
                "index": 0,
                "message": {"role": "assistant", "content": "{{EscapeJsonString(turn.TextResponse ?? DefaultResponse)}}"},
                "finish_reason": "stop"
            }],
            "usage": {
                "prompt_tokens": {{cacheStats.InputTokens}},
                "completion_tokens": {{cacheStats.OutputTokens}},
                "total_tokens": {{cacheStats.InputTokens + cacheStats.OutputTokens}},
                "prompt_cache_hit_tokens": {{cacheHitTokens}},
                "prompt_cache_miss_tokens": {{cacheMissTokens}}
            }
        }
        """;
    }

    public override string BuildStreamChunk(string id, string content, bool isLast)
    {
        if (isLast)
        {
            return $"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{}},\"finish_reason\":\"stop\"}}]}}\n\ndata: [DONE]\n\n";
        }

        return $"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"content\":\"{EscapeJsonString(content)}\"}},\"finish_reason\":null}}]}}\n\n";
    }

    /// <summary>
    /// 流式最终 chunk — 真实 DeepSeek API 在最后一个 chunk 的 usage 中包含
    /// prompt_cache_hit_tokens 和 prompt_cache_miss_tokens 字段。
    /// </summary>
    public override string BuildStreamFinalChunk(string id, CacheStats cacheStats)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(cacheStats);

        return $"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{}},\"finish_reason\":\"stop\"}}],\"usage\":{{\"prompt_tokens\":{cacheStats.InputTokens},\"completion_tokens\":{cacheStats.OutputTokens},\"total_tokens\":{cacheStats.InputTokens + cacheStats.OutputTokens},\"prompt_cache_hit_tokens\":{cacheStats.CacheReadTokens},\"prompt_cache_miss_tokens\":{cacheStats.CacheCreationTokens}}}}}\n\ndata: [DONE]\n\n";
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
            "model": "deepseek-v4-flash",
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

        var first = toolCalls[0];
        var firstId = GenerateToolCallId(first);
        sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"role\":\"assistant\",\"tool_calls\":[{{\"index\":0,\"id\":\"{firstId}\",\"type\":\"function\",\"function\":{{\"name\":\"{first.ToolName}\",\"arguments\":\"\"}}}}]}},\"finish_reason\":null}}]}}\n\n");
        sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"tool_calls\":[{{\"index\":0,\"function\":{{\"arguments\":\"{EscapeJsonString(first.Arguments)}\"}}}}]}},\"finish_reason\":null}}]}}\n\n");

        for (var i = 1; i < toolCalls.Count; i++)
        {
            var tc = toolCalls[i];
            var tcId = GenerateToolCallId(tc);
            sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"tool_calls\":[{{\"index\":{i},\"id\":\"{tcId}\",\"type\":\"function\",\"function\":{{\"name\":\"{tc.ToolName}\",\"arguments\":\"\"}}}}]}},\"finish_reason\":null}}]}}\n\n");
            sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"tool_calls\":[{{\"index\":{i},\"function\":{{\"arguments\":\"{EscapeJsonString(tc.Arguments)}\"}}}}]}},\"finish_reason\":null}}]}}\n\n");
        }

        sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{}},\"finish_reason\":\"tool_calls\"}}]}}\n\ndata: [DONE]\n\n");
        return sb.ToString();
    }

    public override string BuildStreamThinkingResponse(string id)
    {
        var thinking = CurrentTurn.ThinkingContent;
        if (string.IsNullOrEmpty(thinking)) return "";

        // DeepSeek reasoning_content 流式格式
        var sb = new StringBuilder();
        sb.Append($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"reasoning_content\":\"{EscapeJsonString(thinking)}\"}},\"finish_reason\":null}}]}}\n\n");
        return sb.ToString();
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
