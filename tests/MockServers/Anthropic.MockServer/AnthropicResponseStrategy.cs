namespace Anthropic.MockServer;

/// <summary>
/// Anthropic 脚本化响应策略 — 支持预设对话返回、工具调用和思考内容
/// </summary>
public sealed class AnthropicResponseStrategy : ScriptedResponseStrategyBase
{
    public AnthropicResponseStrategy(List<ScriptedTurn>? turns, string defaultResponse)
        : base(turns, defaultResponse) { }

    public override string BuildResponse(JsonElement request, CacheStats cacheStats)
    {
        var turn = CurrentTurn;

        return $$"""
        {
            "id": "msg_{{Guid.NewGuid():N}}",
            "type": "message",
            "role": "assistant",
            "content": [{"type": "text", "text": "{{EscapeJsonString(turn.TextResponse ?? DefaultResponse)}}"}],
            "model": "claude-sonnet-4-20250514",
            "stop_reason": "end_turn",
            "usage": {
                "input_tokens": {{cacheStats.InputTokens}},
                "output_tokens": {{cacheStats.OutputTokens}},
                "cache_creation_input_tokens": {{cacheStats.CacheCreationTokens}},
                "cache_read_input_tokens": {{cacheStats.CacheReadTokens}}
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
            return
                $"event: message_delta\ndata: {{\"type\":\"message_delta\",\"delta\":{{\"stop_reason\":\"end_turn\"}},\"usage\":{{\"output_tokens\":12}}}}\n\n" +
                $"event: message_stop\ndata: {{\"type\":\"message_stop\"}}\n\n";
        }

        return $"event: content_block_delta\ndata: {{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{{\"type\":\"text_delta\",\"text\":\"{EscapeJsonString(content)}\"}}}}\n\n";
    }

    /// <summary>
    /// 流式最终 chunk — 真实 Anthropic API 在 message_delta 事件中包含
    /// cache_creation_input_tokens 和 cache_read_input_tokens 字段。
    /// </summary>
    public override string BuildStreamFinalChunk(string id, CacheStats cacheStats)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(cacheStats);

        return
            $"event: message_delta\ndata: {{\"type\":\"message_delta\",\"delta\":{{\"stop_reason\":\"end_turn\"}},\"usage\":{{\"output_tokens\":{cacheStats.OutputTokens},\"cache_creation_input_tokens\":{cacheStats.CacheCreationTokens},\"cache_read_input_tokens\":{cacheStats.CacheReadTokens}}}}}\n\n" +
            $"event: message_stop\ndata: {{\"type\":\"message_stop\"}}\n\n";
    }

    public override string? BuildStreamPreamble(string id)
    {
        return
            $"event: message_start\ndata: {{\"type\":\"message_start\",\"message\":{{\"id\":\"{id}\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[],\"model\":\"claude-sonnet-4-20250514\",\"stop_reason\":null,\"usage\":{{\"input_tokens\":10,\"output_tokens\":0}}}}}}\n\n" +
            $"event: content_block_start\ndata: {{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{{\"type\":\"text\",\"text\":\"\"}}}}\n\n";
    }

    public override string BuildToolCallResponse(JsonElement request, CacheStats cacheStats)
    {
        var turn = CurrentTurn;
        var toolCalls = turn.ToolCalls ?? [];
        var contentParts = new List<string>();

        foreach (var tc in toolCalls)
        {
            var tcId = GenerateToolCallId(tc);
            contentParts.Add($"{{\"type\":\"tool_use\",\"id\":\"{tcId}\",\"name\":\"{tc.ToolName}\",\"input\":{tc.Arguments}}}");
        }

        return $$"""
        {
            "id": "msg_{{Guid.NewGuid():N}}",
            "type": "message",
            "role": "assistant",
            "content": [{{string.Join(",", contentParts)}}],
            "model": "claude-sonnet-4-20250514",
            "stop_reason": "tool_use",
            "usage": {
                "input_tokens": {{cacheStats.InputTokens}},
                "output_tokens": {{cacheStats.OutputTokens}},
                "cache_creation_input_tokens": {{cacheStats.CacheCreationTokens}},
                "cache_read_input_tokens": {{cacheStats.CacheReadTokens}}
            }
        }
        """;
    }

    public override string BuildStreamToolCallResponse(string id)
    {
        var turn = CurrentTurn;
        var toolCalls = turn.ToolCalls ?? [];
        var sb = new StringBuilder();

        for (var i = 0; i < toolCalls.Count; i++)
        {
            var tc = toolCalls[i];
            var tcId = GenerateToolCallId(tc);

            // content_block_start
            sb.Append($"event: content_block_start\ndata: {{\"type\":\"content_block_start\",\"index\":{i},\"content_block\":{{\"type\":\"tool_use\",\"id\":\"{tcId}\",\"name\":\"{tc.ToolName}\",\"input\":{{}}}}}}\n\n");

            // content_block_delta (参数)
            sb.Append($"event: content_block_delta\ndata: {{\"type\":\"content_block_delta\",\"index\":{i},\"delta\":{{\"type\":\"input_json_delta\",\"partial_json\":\"{EscapeJsonString(tc.Arguments)}\"}}}}\n\n");

            // content_block_stop
            sb.Append($"event: content_block_stop\ndata: {{\"type\":\"content_block_stop\",\"index\":{i}}}\n\n");
        }

        // message_delta + message_stop
        sb.Append($"event: message_delta\ndata: {{\"type\":\"message_delta\",\"delta\":{{\"stop_reason\":\"tool_use\"}},\"usage\":{{\"output_tokens\":20}}}}\n\n");
        sb.Append($"event: message_stop\ndata: {{\"type\":\"message_stop\"}}\n\n");
        return sb.ToString();
    }

    public override string BuildStreamThinkingResponse(string id)
    {
        var thinking = CurrentTurn.ThinkingContent;
        if (string.IsNullOrEmpty(thinking)) return "";

        // Anthropic thinking 流式格式
        var sb = new StringBuilder();
        sb.Append($"event: content_block_start\ndata: {{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{{\"type\":\"thinking\",\"thinking\":\"\"}}}}\n\n");
        sb.Append($"event: content_block_delta\ndata: {{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{{\"type\":\"thinking_delta\",\"thinking\":\"{EscapeJsonString(thinking)}\"}}}}\n\n");
        sb.Append($"event: content_block_stop\ndata: {{\"type\":\"content_block_stop\",\"index\":0}}\n\n");
        return sb.ToString();
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
