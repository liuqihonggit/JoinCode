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
        var hasToolResult = HasToolResultMessage(root);

        if (hasToolResult) {
            const string finalReply = "工具执行完毕。这是模拟 AI 的最终回应。";
            const string finalDesc = "工具结果→最终回应";
            if (protocol == "anthropic") {
                return (new InjectDto { Body = BuildAnthropicResponse(finalReply) }, $"{finalDesc} [anthropic]");
            }
            if (isStream) {
                return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = BuildOpenAiStreamChunks(finalReply) }, $"{finalDesc} [stream]");
            }
            return (new InjectDto { Body = BuildOpenAiResponse(finalReply) }, finalDesc);
        }

        var (toolCall, toolDesc) = MatchToolCallIntent(userMessage);
        if (toolCall is not null) {
            if (protocol == "openai" && isStream) {
                return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = BuildOpenAiToolCallStreamChunks(toolCall) }, $"{toolDesc} [stream]");
            }
            return (new InjectDto { Body = BuildOpenAiToolCallResponse(toolCall) }, toolDesc);
        }

        var (replyText, desc) = MatchIntent(userMessage);

        if (protocol == "anthropic") {
            var body = BuildAnthropicResponse(replyText);
            return (new InjectDto { Body = body }, $"{desc} [anthropic]");
        }

        if (isStream) {
            var chunks = BuildOpenAiStreamChunks(replyText);
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, $"{desc} [stream]");
        }
        var nonStreamBody = BuildOpenAiResponse(replyText);
        return (new InjectDto { Body = nonStreamBody }, desc);
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

    /// <summary>检查请求是否包含 tool 角色消息（工具执行结果回传）</summary>
    private static bool HasToolResultMessage(JsonElement root) {
        if (!root.TryGetProperty("messages", out var messages))
            return false;
        foreach (var msg in messages.EnumerateArray()) {
            if (msg.TryGetProperty("role", out var r) && r.GetString() == "tool")
                return true;
        }
        return false;
    }

    /// <summary>匹配工具调用意图 — 返回工具调用 DTO 或 null</summary>
    private static (ToolCallDto? ToolCall, string Description) MatchToolCallIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();
        if (lower.Contains("read file") || lower.Contains("读文件") || lower.Contains("f1")) {
            return (new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = """{"file_path": "/nonexistent/missing_file.txt"}"""
                }
            }, "F1: 工具调用失败（read 不存在的文件）");
        }
        if (lower.Contains("multi tool") || lower.Contains("多工具") || lower.Contains("f3")) {
            return (new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = """{"file_path": "/nonexistent/file1.txt"}"""
                }
            }, "F3: 多工具连续调用（第1个）");
        }
        return (null, "");
    }

    private static string BuildOpenAiToolCallResponse(ToolCallDto toolCall) {
        var resp = new OpenAiResponse {
            Id = $"chatcmpl-{Guid.NewGuid():N}"[..30],
            Choices = [new OpenAiChoice {
                Index = 0,
                Message = new OpenAiMessage { Content = null, ToolCalls = [toolCall] },
                FinishReason = "tool_calls"
            }]
        };
        return JsonSerializer.Serialize(resp, JsonOpts);
    }

    private static List<string> BuildOpenAiToolCallStreamChunks(ToolCallDto toolCall) {
        var id = $"chatcmpl-{Guid.NewGuid():N}"[..30];
        var chunks = new List<string>();

        chunks.Add(SseLine(new StreamChunk {
            Id = id,
            Choices = [new StreamChoice {
                Index = 0,
                Delta = new StreamDelta {
                    Role = "assistant",
                    ToolCalls = [new StreamToolCallDelta {
                        Index = 0,
                        Id = toolCall.Id,
                        Type = "function",
                        Function = new StreamToolCallFunctionDelta { Name = toolCall.Function.Name, Arguments = "" }
                    }]
                }
            }]
        }));

        chunks.Add(SseLine(new StreamChunk {
            Id = id,
            Choices = [new StreamChoice {
                Index = 0,
                Delta = new StreamDelta {
                    ToolCalls = [new StreamToolCallDelta {
                        Index = 0,
                        Function = new StreamToolCallFunctionDelta { Arguments = toolCall.Function.Arguments }
                    }]
                }
            }]
        }));

        chunks.Add(SseLine(new StreamChunk {
            Id = id,
            Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "tool_calls" }]
        }));
        chunks.Add("data: [DONE]\n\n");

        return chunks;
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
