namespace Responses.MockServer;

/// <summary>
/// Responses API 脚本化响应策略 — 返回 OpenAI/DeepSeek Responses API 格式(POST /responses)
/// 用 output 数组(message/function_call items)+ output_text 便捷字段
/// 流式用语义化 SSE 事件(response.created/response.output_text.delta/response.completed),无 data: [DONE]
/// </summary>
public sealed class ResponsesResponseStrategy : ScriptedResponseStrategyBase
{
    public ResponsesResponseStrategy(List<ScriptedTurn>? turns, string defaultResponse)
        : base(turns, defaultResponse) { }

    public override string BuildResponse(JsonElement request, CacheStats cacheStats)
    {
        var turn = CurrentTurn;
        var text = turn.TextResponse ?? DefaultResponse;

        if (turn.ToolCalls is { Count: > 0 })
        {
            var toolCallsJson = BuildToolCallOutputItems(turn.ToolCalls);
            return $$"""
            {
                "id": "resp-{{Guid.NewGuid():N}}",
                "object": "response",
                "model": "deepseek-v4-flash",
                "status": "completed",
                "output": [{{toolCallsJson}}],
                "usage": {
                    "input_tokens": {{cacheStats.InputTokens}},
                    "output_tokens": {{cacheStats.OutputTokens}}
                }
            }
            """;
        }

        return $$"""
        {
            "id": "resp-{{Guid.NewGuid():N}}",
            "object": "response",
            "model": "deepseek-v4-flash",
            "status": "completed",
            "output": [{
                "type": "message",
                "role": "assistant",
                "content": [{"type": "output_text", "text": "{{EscapeJsonString(text)}}"}]
            }],
            "output_text": "{{EscapeJsonString(text)}}",
            "usage": {
                "input_tokens": {{cacheStats.InputTokens}},
                "output_tokens": {{cacheStats.OutputTokens}}
            }
        }
        """;
    }

    public override string? BuildStreamPreamble(string id)
    {
        return $"event: response.created\ndata: {{\"id\":\"{id}\",\"object\":\"response\",\"status\":\"in_progress\"}}\n\n";
    }

    public override string BuildStreamChunk(string id, string content, bool isLast)
    {
        if (isLast)
            return "";

        return $"event: response.output_text.delta\ndata: {{\"delta\":\"{EscapeJsonString(content)}\"}}\n\n";
    }

    public override string BuildStreamFinalChunk(string id, CacheStats cacheStats)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(cacheStats);

        return $"event: response.completed\ndata: {{\"response\":{{\"id\":\"{id}\",\"object\":\"response\",\"status\":\"completed\",\"usage\":{{\"input_tokens\":{cacheStats.InputTokens},\"output_tokens\":{cacheStats.OutputTokens}}}}}}}\n\n";
    }

    public override string BuildToolCallResponse(JsonElement request, CacheStats cacheStats)
    {
        var turn = CurrentTurn;
        var toolCalls = turn.ToolCalls ?? [];
        var toolCallsJson = BuildToolCallOutputItems(toolCalls);

        return $$"""
        {
            "id": "resp-{{Guid.NewGuid():N}}",
            "object": "response",
            "model": "deepseek-v4-flash",
            "status": "completed",
            "output": [{{toolCallsJson}}],
            "usage": {
                "input_tokens": {{cacheStats.InputTokens}},
                "output_tokens": {{cacheStats.OutputTokens}}
            }
        }
        """;
    }

    public override string BuildStreamToolCallResponse(string id)
    {
        var turn = CurrentTurn;
        var toolCalls = turn.ToolCalls ?? [];
        var sb = new StringBuilder();

        foreach (var tc in toolCalls)
        {
            var callId = GenerateToolCallId(tc);
            sb.Append($"event: response.output_item.added\ndata: {{\"item\":{{\"type\":\"function_call\",\"id\":\"fc_{Guid.NewGuid():N}\",\"call_id\":\"{callId}\",\"name\":\"{tc.ToolName}\",\"arguments\":\"\"}}}}\n\n");
            sb.Append($"event: response.function_call_arguments.delta\ndata: {{\"item_id\":\"fc_{callId}\",\"delta\":\"{EscapeJsonString(tc.Arguments)}\"}}\n\n");
        }

        sb.Append($"event: response.completed\ndata: {{\"response\":{{\"id\":\"{id}\",\"object\":\"response\",\"status\":\"completed\",\"usage\":{{\"input_tokens\":0,\"output_tokens\":0}}}}}}\n\n");
        return sb.ToString();
    }

    public override string BuildStreamThinkingResponse(string id)
    {
        var thinking = CurrentTurn.ThinkingContent;
        if (string.IsNullOrEmpty(thinking)) return "";

        return $"event: response.reasoning_text.delta\ndata: {{\"delta\":\"{EscapeJsonString(thinking)}\"}}\n\n";
    }

    private static string BuildToolCallOutputItems(List<ToolCallConfig> toolCalls)
    {
        var parts = new List<string>();
        foreach (var tc in toolCalls)
        {
            var callId = !string.IsNullOrEmpty(tc.ToolCallId) ? tc.ToolCallId : $"call_{Guid.NewGuid():N}";
            parts.Add($"{{\"type\":\"function_call\",\"id\":\"fc_{Guid.NewGuid():N}\",\"call_id\":\"{callId}\",\"name\":\"{tc.ToolName}\",\"arguments\":\"{EscapeJsonString(tc.Arguments)}\"}}");
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
