namespace Responses.MockServer;

/// <summary>
/// Responses API 脚本化响应策略 — 返回 OpenAI/DeepSeek Responses API 格式(POST /responses)
/// 用 output 数组(message/function_call items)+ output_text 便捷字段
/// 流式用语义化 SSE 事件(response.created/response.output_text.delta/response.completed),无 data: [DONE]
/// </summary>
public sealed class ResponsesResponseStrategy : ScriptedResponseStrategyBase
{
    private readonly bool _enforceThinkingRoundTrip;

    public ResponsesResponseStrategy(List<ScriptedTurn>? turns, string defaultResponse, bool enforceThinkingRoundTrip = false)
        : base(turns, defaultResponse)
    {
        _enforceThinkingRoundTrip = enforceThinkingRoundTrip;
    }

    /// <summary>
    /// 模拟真实 DeepSeek 思考链回传校验 — 400 错误文案
    /// </summary>
    private const string ThinkingRoundTripError = "The reasoning_text in the thinking mode must be passed back to the API.";

    /// <summary>
    /// 模拟真实 DeepSeek 行为: thinking 模式下历史含 assistant 消息但缺失 reasoning 回传 → 400。
    /// 仅在 EnforceThinkingRoundTrip 开启且为 Responses 协议请求时生效。
    /// </summary>
    public override int GetHttpStatusCode(JsonElement request)
    {
        if (!_enforceThinkingRoundTrip)
            return base.GetHttpStatusCode(request);

        if (request.TryGetProperty("reasoning", out var reasoningProp) && reasoningProp.ValueKind == JsonValueKind.Object)
        {
            if (HasMissingReasoningRoundTrip(request))
                return 400;
        }

        return base.GetHttpStatusCode(request);
    }

    /// <summary>
    /// 判定请求历史是否缺失 reasoning 回传:
    /// input 中存在 assistant message 但没有任何 reasoning item。
    /// </summary>
    private static bool HasMissingReasoningRoundTrip(JsonElement request)
    {
        if (!request.TryGetProperty("input", out var inputProp) || inputProp.ValueKind != JsonValueKind.Array)
            return false;

        var hasAssistantMessage = false;
        var hasReasoningItem = false;

        foreach (var item in inputProp.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

            if (type == "reasoning")
            {
                hasReasoningItem = true;
            }
            else if (type == "message"
                     && item.TryGetProperty("role", out var roleProp)
                     && roleProp.GetString() == "assistant")
            {
                hasAssistantMessage = true;
            }
        }

        return hasAssistantMessage && !hasReasoningItem;
    }

    public override string BuildResponse(JsonElement request, CacheStats cacheStats)
    {
        if (GetHttpStatusCode(request) == 400)
        {
            return $$"""
            {
                "error": {
                    "type": "invalid_request_error",
                    "message": "{{ThinkingRoundTripError}}",
                    "param": null,
                    "code": null
                }
            }
            """;
        }

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
                    "output_tokens": {{cacheStats.OutputTokens}},
                    "input_tokens_details": {
                        "cached_tokens": {{cacheStats.CacheReadTokens}}
                    }
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
                "output_tokens": {{cacheStats.OutputTokens}},
                "input_tokens_details": {
                    "cached_tokens": {{cacheStats.CacheReadTokens}}
                }
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

        return $"event: response.completed\ndata: {{\"response\":{{\"id\":\"{id}\",\"object\":\"response\",\"status\":\"completed\",\"usage\":{{\"input_tokens\":{cacheStats.InputTokens},\"output_tokens\":{cacheStats.OutputTokens},\"input_tokens_details\":{{\"cached_tokens\":{cacheStats.CacheReadTokens}}}}}}}}}\n\n";
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
                "output_tokens": {{cacheStats.OutputTokens}},
                "input_tokens_details": {
                    "cached_tokens": {{cacheStats.CacheReadTokens}}
                }
            }
        }
        """;
    }

    public override string BuildStreamToolCallResponse(string id, CacheStats cacheStats)
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

        sb.Append($"event: response.completed\ndata: {{\"response\":{{\"id\":\"{id}\",\"object\":\"response\",\"status\":\"completed\",\"usage\":{{\"input_tokens\":{cacheStats.InputTokens},\"output_tokens\":{cacheStats.OutputTokens},\"input_tokens_details\":{{\"cached_tokens\":{cacheStats.CacheReadTokens}}}}}}}}}\n\n");
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
