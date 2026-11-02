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
        var toolResultCount = CountToolResultMessages(root);

        if (toolResultCount > 0) {
            var (nextToolCall, nextDesc) = MatchNextToolCallIntent(userMessage, toolResultCount);
            if (nextToolCall is not null && protocol == "openai" && isStream) {
                return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = BuildOpenAiToolCallStreamChunks([nextToolCall]) }, $"{nextDesc} [stream]");
            }
            if (nextToolCall is not null) {
                return (new InjectDto { Body = BuildOpenAiToolCallResponse([nextToolCall]) }, nextDesc);
            }

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

        var (toolCalls, toolDesc) = MatchToolCallIntent(userMessage);
        if (toolCalls is not null) {
            if (protocol == "openai" && isStream) {
                return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = BuildOpenAiToolCallStreamChunks(toolCalls) }, $"{toolDesc} [stream]");
            }
            return (new InjectDto { Body = BuildOpenAiToolCallResponse(toolCalls) }, toolDesc);
        }

        var (replyText, desc) = MatchIntent(userMessage);

        if (protocol == "anthropic") {
            var body = BuildAnthropicResponse(replyText);
            return (new InjectDto { Body = body }, $"{desc} [anthropic]");
        }

        if (isStream) {
            var chunks = BuildOpenAiStreamChunks(replyText);
            if (userMessage.ToLowerInvariant().Contains("l4") || userMessage.ToLowerInvariant().Contains("stream interrupt") || userMessage.ToLowerInvariant().Contains("流式中断")) {
                chunks.RemoveAt(chunks.Count - 1);
                chunks.RemoveAt(chunks.Count - 1);
                return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, $"{desc} [stream, L4 中断无DONE]");
            }
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
        if (lower.Contains("unicode") || lower.Contains("emoji") || lower.Contains("h4")) {
            return ("Unicode 测试：中文 日本語 한국어 العربية \U0001F600 \U0001F680 \u2764\uFE0F \u00A9 \u2122 ✅❌⚡", "H4: Unicode/Emoji");
        }
        if (lower.Contains("超长") || lower.Contains("long response") || lower.Contains("j1")) {
            var sb = new StringBuilder("J1 超长响应测试开始\n");
            for (var i = 0; i < 100; i++)
                sb.AppendLine($"行 {i + 1}: 这是一段重复的测试文本，用于验证 jcc 对超长响应的处理能力。编号 {i + 1}。");
            sb.AppendLine("超长响应测试结束。");
            return (sb.ToString(), "J1: 超长响应");
        }
        if (string.IsNullOrWhiteSpace(userMessage)) {
            return ("（收到空消息）", "空消息");
        }
        return ($"你说的是\"{userMessage}\"。我收到了。这是模拟回应。", "回声");
    }

    /// <summary>统计 tool 角色消息数量（工具执行结果回传次数）</summary>
    private static int CountToolResultMessages(JsonElement root) {
        if (!root.TryGetProperty("messages", out var messages))
            return 0;
        var count = 0;
        foreach (var msg in messages.EnumerateArray()) {
            if (msg.TryGetProperty("role", out var r) && r.GetString() == "tool")
                count++;
        }
        return count;
    }

    /// <summary>多轮工具调用：根据已完成的工具结果数决定下一步</summary>
    private static (ToolCallDto? ToolCall, string Description) MatchNextToolCallIntent(string userMessage, int toolResultCount) {
        var lower = userMessage.ToLowerInvariant();
        if (lower.Contains("multi tool") || lower.Contains("多工具") || lower.Contains("f3")) {
            if (toolResultCount == 1) {
                return (new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto {
                        Name = "read",
                        Arguments = """{"file_path": "/nonexistent/file2.txt"}"""
                    }
                }, "F3: 多工具连续调用（第2个）");
            }
        }
        return (null, "");
    }

    /// <summary>匹配工具调用意图 — 返回工具调用列表或 null（null=不匹配，空列表=I1空tool_calls）</summary>
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchToolCallIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();
        if (lower.Contains("read file") || lower.Contains("读文件") || lower.Contains("f1")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = """{"file_path": "/nonexistent/missing_file.txt"}"""
                }
            }], "F1: 工具调用失败（read 不存在的文件）");
        }
        if (lower.Contains("multi tool") || lower.Contains("多工具") || lower.Contains("f3")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = """{"file_path": "/nonexistent/file1.txt"}"""
                }
            }], "F3: 多工具连续调用（第1个）");
        }
        if (lower.Contains("empty tool") || lower.Contains("空工具") || lower.Contains("i1")) {
            return ([], "I1: 空工具调用（tool_calls=[]）");
        }
        if (lower.Contains("multi call") || lower.Contains("多调用") || lower.Contains("i2")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto {
                        Name = "read",
                        Arguments = """{"file_path": "/nonexistent/i2_file1.txt"}"""
                    }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto {
                        Name = "read",
                        Arguments = """{"file_path": "/nonexistent/i2_file2.txt"}"""
                    }
                }
            ], "I2: 多个工具调用（一次2个）");
        }
        if (lower.Contains("tool error") || lower.Contains("工具错误") || lower.Contains("i3")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "exit 1"}"""
                }
            }], "I3: 工具执行错误（bash exit 1）");
        }
        if (lower.Contains("tool timeout") || lower.Contains("工具超时") || lower.Contains("f2")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "sleep 10 && echo done"}"""
                }
            }], "F2: 工具执行慢（bash sleep 10）");
        }
        if (lower.Contains("long output") || lower.Contains("长输出") || lower.Contains("l6")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "seq 1 500"}"""
                }
            }], "L6: 工具返回超长结果（seq 1 500）");
        }
        if (lower.Contains("special chars") || lower.Contains("特殊字符") || lower.Contains("l11")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'hello \"world\" \n newline \t tab \\ backslash'}"""
                }
            }], "L11: 工具参数特殊字符（引号/换行/制表/反斜杠）");
        }
        if (lower.Contains("unicode args") || lower.Contains("unicode参数") || lower.Contains("l12")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '中文 日本語 한국어 \U0001F600 emoji'}"""
                }
            }], "L12: 工具参数 Unicode/emoji");
        }
        if (lower.Contains("bad args") || lower.Contains("参数错误") || lower.Contains("k1")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = """{"file_path": /missing/quotes.json}"""
                }
            }], "K1: 工具参数格式错误（JSON 缺引号）");
        }
        if (lower.Contains("missing param") || lower.Contains("缺参数") || lower.Contains("k2")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = "{}"
                }
            }], "K2: 工具参数缺失（空对象无 file_path）");
        }
        return (null, "");
    }

    private static string BuildOpenAiToolCallResponse(List<ToolCallDto> toolCalls) {
        var resp = new OpenAiResponse {
            Id = $"chatcmpl-{Guid.NewGuid():N}"[..30],
            Choices = [new OpenAiChoice {
                Index = 0,
                Message = new OpenAiMessage { Content = null, ToolCalls = toolCalls },
                FinishReason = toolCalls.Count == 0 ? "stop" : "tool_calls"
            }]
        };
        return JsonSerializer.Serialize(resp, JsonOpts);
    }

    private static List<string> BuildOpenAiToolCallStreamChunks(List<ToolCallDto> toolCalls) {
        var id = $"chatcmpl-{Guid.NewGuid():N}"[..30];
        var chunks = new List<string>();

        if (toolCalls.Count == 0) {
            chunks.Add(SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant" } }] }));
            chunks.Add(SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }));
            chunks.Add("data: [DONE]\n\n");
            return chunks;
        }

        chunks.Add(SseLine(new StreamChunk {
            Id = id,
            Choices = [new StreamChoice {
                Index = 0,
                Delta = new StreamDelta {
                    Role = "assistant",
                    ToolCalls = [new StreamToolCallDelta {
                        Index = 0,
                        Id = toolCalls[0].Id,
                        Type = "function",
                        Function = new StreamToolCallFunctionDelta { Name = toolCalls[0].Function.Name, Arguments = "" }
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
                        Function = new StreamToolCallFunctionDelta { Arguments = toolCalls[0].Function.Arguments }
                    }]
                }
            }]
        }));

        for (var i = 1; i < toolCalls.Count; i++) {
            chunks.Add(SseLine(new StreamChunk {
                Id = id,
                Choices = [new StreamChoice {
                    Index = 0,
                    Delta = new StreamDelta {
                        ToolCalls = [new StreamToolCallDelta {
                            Index = i,
                            Id = toolCalls[i].Id,
                            Type = "function",
                            Function = new StreamToolCallFunctionDelta { Name = toolCalls[i].Function.Name, Arguments = "" }
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
                            Index = i,
                            Function = new StreamToolCallFunctionDelta { Arguments = toolCalls[i].Function.Arguments }
                        }]
                    }
                }]
            }));
        }

        chunks.Add(SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "tool_calls" }] }));
        chunks.Add("data: [DONE]\n\n");

        return chunks;
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

        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            var line = i < lines.Length - 1 ? lines[i] + "\n" : lines[i];
            if (line.Length > 0) {
                chunks.Add(SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = line } }] }));
            }
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
