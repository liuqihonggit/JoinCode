namespace MockAiResponder;

/// <summary>
/// 意图匹配器 — 解析 jcc 请求，按用户消息内容生成模拟 AI 响应。
/// 支持 OpenAI（流式+非流式）和 Anthropic（非流式）协议。
/// </summary>
internal static class IntentMatcher {
    private static readonly JsonSerializerOptions JsonOpts = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>根据请求体生成注入响应</summary>
    public static (InjectDto Response, string Description) Generate(string requestBody) {
        using var doc = JsonDocument.Parse(requestBody);
        var root = doc.RootElement;

        var isStream = root.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.True;
        var protocol = root.TryGetProperty("input", out _) ? "responses"
                     : root.TryGetProperty("system", out _) ? "anthropic"
                     : "openai";

        var userMessage = ExtractLastUserMessage(root);
        var (replyText, description) = MatchIntent(userMessage);

        if (protocol == "anthropic") {
            var body = BuildAnthropicResponse(replyText);
            return (new InjectDto { Body = body }, $"{description} [anthropic]");
        }

        if (isStream) {
            var chunks = BuildOpenAiStreamChunks(replyText);
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, $"{description} [stream]");
        }
        var nonStreamBody = BuildOpenAiResponse(replyText);
        return (new InjectDto { Body = nonStreamBody }, description);
    }

    /// <summary>提取最后一条 user 消息（供日志显示）</summary>
    public static string ExtractLastUserMessage(string requestBody) {
        try {
            using var doc = JsonDocument.Parse(requestBody);
            return ExtractLastUserMessage(doc.RootElement);
        } catch {
            return "";
        }
    }

    private static string ExtractLastUserMessage(JsonElement root) {
        string? ExtractFrom(JsonElement arr) {
            string? last = null;
            foreach (var msg in arr.EnumerateArray()) {
                var role = msg.TryGetProperty("role", out var r) ? r.GetString() : null;
                if (role == "user") {
                    var content = msg.TryGetProperty("content", out var c)
                        ? c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetRawText()
                        : null;
                    if (content is not null) last = content;
                }
            }
            return last;
        }

        if (root.TryGetProperty("messages", out var messages)) return ExtractFrom(messages) ?? "";
        if (root.TryGetProperty("input", out var input)) return ExtractFrom(input) ?? "";
        return "";
    }

    private static (string Reply, string Description) MatchIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();
        if (lower.Contains("你好") || lower.Contains("hello") || lower.Contains("hi")) {
            return ("你好！我是 MockServer 模拟 AI。你可以跟我聊天，我会回应你。试试说\"写个函数\"或\"解释什么\"。", "问候");
        }
        if (lower.Contains("写") || lower.Contains("code") || lower.Contains("函数") || lower.Contains("function")) {
            return ("好的，这是一个示例函数：\n\n```csharp\npublic static int Add(int a, int b) => a + b;\n```\n\n调用方式：`Add(1, 2)` 返回 `3`。", "代码示例");
        }
        if (lower.Contains("工具") || lower.Contains("tool")) {
            return ("我支持工具调用。但当前是模拟模式，不会真正执行工具。你可以继续对话。", "工具说明");
        }
        if (lower.Contains("解释") || lower.Contains("explain") || lower.Contains("什么")) {
            return ($"关于\"{userMessage}\"：这是一个模拟回应。真实 AI 会给你详细解释。当前用于调试 jcc 对话流程。", "解释");
        }
        if (string.IsNullOrWhiteSpace(userMessage)) {
            return ("（收到空消息）", "空消息");
        }
        return ($"你说的是\"{userMessage}\"。我收到了。这是模拟回应。", "回声");
    }

    private static string BuildOpenAiResponse(string content) {
        var resp = new OpenAiResponse {
            Id = $"chatcmpl-{Guid.NewGuid():N}"[..30],
            Choices = [new OpenAiChoice { Index = 0, Message = new OpenAiMessage { Content = content } }]
        };
        return JsonSerializer.Serialize(resp, JsonOpts);
    }

    private static List<string> BuildOpenAiStreamChunks(string content) {
        var id = $"chatcmpl-{Guid.NewGuid():N}"[..30];
        var chunks = new List<string>();

        chunks.Add(SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant" } }] }));

        foreach (var ch in content) {
            chunks.Add(SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = ch.ToString() } }] }));
        }

        chunks.Add(SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }));
        chunks.Add("data: [DONE]\n\n");

        return chunks;
    }

    private static string SseLine(StreamChunk chunk) => $"data: {JsonSerializer.Serialize(chunk, JsonOpts)}\n\n";

    private static string BuildAnthropicResponse(string content) {
        var dto = new AnthropicResponse {
            Id = $"msg_{Guid.NewGuid():N}"[..24],
            Model = "mock-claude",
            Content = [new AnthropicContent { Text = content }]
        };
        return JsonSerializer.Serialize(dto, JsonOpts);
    }
}

internal sealed class AnthropicResponse {
    public string Id { get; set; } = "";
    public string Type { get; set; } = "message";
    public string Role { get; set; } = "assistant";
    public string Model { get; set; } = "";
    public List<AnthropicContent> Content { get; set; } = [];
    public string StopReason { get; set; } = "end_turn";
    public AnthropicUsage Usage { get; set; } = new();
}

internal sealed class AnthropicContent {
    public string Type { get; set; } = "text";
    public string Text { get; set; } = "";
}

internal sealed class AnthropicUsage {
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}
