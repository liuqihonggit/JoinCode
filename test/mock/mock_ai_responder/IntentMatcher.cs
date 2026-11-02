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

        if (toolResultCount == 0) {
            var earlyReturn = HandleNoToolResult(root, userMessage, protocol, isStream);
            if (earlyReturn is not null) return earlyReturn.Value;
        }

        if (toolResultCount > 0) {
            var m4Check = CheckM4ToolCallId(root, userMessage, toolResultCount, protocol, isStream);
            if (m4Check is not null) return m4Check.Value;

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

    /// <summary>提取前一条 assistant 消息内容（非空、非工具调用），用于多轮对话记忆测试</summary>
    private static string? ExtractPreviousAssistantMessage(JsonElement root) {
        if (!root.TryGetProperty("messages", out var messages))
            return null;
        string? last = null;
        foreach (var msg in messages.EnumerateArray()) {
            if (msg.TryGetProperty("role", out var r) && r.GetString() == "assistant") {
                var content = msg.TryGetProperty("content", out var c)
                    ? c.ValueKind == JsonValueKind.String ? c.GetString() : null
                    : null;
                if (!string.IsNullOrEmpty(content)) last = content;
            }
        }
        return last;
    }

    /// <summary>M4: 检查 tool 消息的 tool_call_id 是否与 LLM 返回的 id 匹配</summary>
    private static (InjectDto Response, string Description)? CheckM4ToolCallId(JsonElement root, string userMessage, int toolResultCount, string protocol, bool isStream) {
        var lower = userMessage.ToLowerInvariant();
        if (!lower.Contains("m4") && !lower.Contains("id mismatch") && !lower.Contains("id不匹配"))
            return null;

        const string expectedId = "call_m4_fixed_id";
        var actualIds = ExtractToolCallIds(root);
        if (actualIds.Count == 0) return null;

        var actualId = actualIds[0];
        string reply;
        string desc;
        if (actualId == expectedId) {
            reply = "M4: tool_call_id 匹配正确，工具结果已正确关联。这是模拟 AI 的最终回应。";
            desc = $"M4: tool_call_id 匹配正确 (id={actualId})";
        } else {
            reply = $"M4: tool_call_id 不匹配！期望 {expectedId}，实际得到 {actualId}。这可能导致对话上下文断裂。";
            desc = $"M4: tool_call_id 不匹配 (期望={expectedId}, 实际={actualId})";
        }

        if (protocol == "anthropic") {
            return (new InjectDto { Body = BuildAnthropicResponse(reply) }, $"{desc} [anthropic]");
        }
        if (isStream) {
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = BuildOpenAiStreamChunks(reply) }, $"{desc} [stream]");
        }
        return (new InjectDto { Body = BuildOpenAiResponse(reply) }, desc);
    }

    /// <summary>提取所有 tool 角色消息的 tool_call_id</summary>
    private static List<string> ExtractToolCallIds(JsonElement root) {
        var ids = new List<string>();
        if (!root.TryGetProperty("messages", out var messages))
            return ids;
        foreach (var msg in messages.EnumerateArray()) {
            if (msg.TryGetProperty("role", out var r) && r.GetString() == "tool") {
                if (msg.TryGetProperty("tool_call_id", out var idProp) && idProp.ValueKind == JsonValueKind.String) {
                    ids.Add(idProp.GetString() ?? "");
                }
            }
        }
        return ids;
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
        if (lower.Contains("many tools") || lower.Contains("多工具链") || lower.Contains("m3")) {
            if (toolResultCount < 5) {
                var args = JsonSerializer.Serialize(new { file_path = $"/nonexistent/m3_file{toolResultCount + 1}.txt" });
                return (new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto {
                        Name = "read",
                        Arguments = args
                    }
                }, $"M3: 5轮工具调用（第{toolResultCount + 1}个）");
            }
        }
        if (lower.Contains("m5") || lower.Contains("nested tool") || lower.Contains("嵌套工具")) {
            if (toolResultCount == 1) {
                return (new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto {
                        Name = "bash",
                        Arguments = """{"command": "echo m5_step2_done"}"""
                    }
                }, "M5: 嵌套工具调用（第2个，基于第1个结果）");
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
        if (lower.Contains("many tools") || lower.Contains("多工具链") || lower.Contains("m3")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = """{"file_path": "/nonexistent/m3_file1.txt"}"""
                }
            }], "M3: 5轮工具调用（第1个）");
        }
        if (lower.Contains("m4") || lower.Contains("id mismatch") || lower.Contains("id不匹配")) {
            return ([new ToolCallDto {
                Id = "call_m4_fixed_id",
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = """{"file_path": "/nonexistent/m4_test.txt"}"""
                }
            }], "M4: 工具调用 ID 匹配测试（固定 id=call_m4_fixed_id）");
        }
        if (lower.Contains("m5") || lower.Contains("nested tool") || lower.Contains("嵌套工具")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo m5_step1_done"}"""
                }
            }], "M5: 嵌套工具调用（第1个）");
        }
        if (lower.Contains("n1") || lower.Contains("unknown tool") || lower.Contains("不存在工具")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "nonexistent_tool_xyz",
                    Arguments = """{"arg": "test"}"""
                }
            }], "N1: 不存在的工具名");
        }
        if (lower.Contains("n2") || lower.Contains("empty name") || lower.Contains("空名工具")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "",
                    Arguments = """{"arg": "test"}"""
                }
            }], "N2: 空工具名");
        }
        if (lower.Contains("n3") || lower.Contains("bad json args") || lower.Contains("非json参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "this is not json {{{"
                }
            }], "N3: 工具参数非 JSON");
        }
        if (lower.Contains("n4") || lower.Contains("huge args") || lower.Contains("超大参数")) {
            var bigArg = new string('x', 10000);
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = JsonSerializer.Serialize(new { command = $"echo {bigArg}" })
                }
            }], "N4: 超大工具参数（10KB）");
        }
        if (lower.Contains("p4") || lower.Contains("empty args") || lower.Contains("空参数串")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = ""
                }
            }], "P4: 工具参数为空字符串");
        }
        if (lower.Contains("p5") || lower.Contains("null args") || lower.Contains("null参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "null"
                }
            }], "P5: 工具参数为 null 字面量");
        }
        if (lower.Contains("p6") || lower.Contains("long id") || lower.Contains("超长id")) {
            var longId = new string('a', 200);
            return ([new ToolCallDto {
                Id = longId,
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo p6"}"""
                }
            }], "P6: 超长 tool_call_id（200字符）");
        }
        if (lower.Contains("q2") || lower.Contains("upper case") || lower.Contains("大写工具名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "BASH",
                    Arguments = """{"command": "echo q2"}"""
                }
            }], "Q2: 大写工具名 BASH");
        }
        if (lower.Contains("q3") || lower.Contains("whitespace name") || lower.Contains("空格工具名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = " bash ",
                    Arguments = """{"command": "echo q3"}"""
                }
            }], "Q3: 前后空格工具名");
        }
        if (lower.Contains("q4") || lower.Contains("dup id") || lower.Contains("重复id")) {
            var dupId = $"call_{Guid.NewGuid():N}"[..20];
            return ([
                new ToolCallDto {
                    Id = dupId,
                    Function = new ToolCallFunctionDto {
                        Name = "bash",
                        Arguments = """{"command": "echo q4_first"}"""
                    }
                },
                new ToolCallDto {
                    Id = dupId,
                    Function = new ToolCallFunctionDto {
                        Name = "bash",
                        Arguments = """{"command": "echo q4_second"}"""
                    }
                }
            ], "Q4: 重复 tool_call_id");
        }
        if (lower.Contains("r1") || lower.Contains("ansi output") || lower.Contains("ansi输出")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf '\\033[31mred\\033[0m text'"}"""
                }
            }], "R1: 工具结果包含 ANSI 转义序列");
        }
        if (lower.Contains("r2") || lower.Contains("null byte") || lower.Contains("null字节")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf 'before\\x00after'"}"""
                }
            }], "R2: 工具结果包含 null 字节");
        }
        if (lower.Contains("r3") || lower.Contains("long line") || lower.Contains("超长行")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf '%0.sx' {1..5000}"}"""
                }
            }], "R3: 工具结果超长行（5000字符无换行）");
        }
        if (lower.Contains("s3") || lower.Contains("content and tool") || lower.Contains("文本加工具")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo s3_done"}"""
                }
            }], "S3: content+tool_call 同时出现（附带文本）");
        }
        if (lower.Contains("t1") || lower.Contains("dot name") || lower.Contains("点号工具名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read.file",
                    Arguments = """{"file_path": "/nonexistent/t1.txt"}"""
                }
            }], "T1: 工具名包含点号 read.file");
        }
        if (lower.Contains("t2") || lower.Contains("dash name") || lower.Contains("连字符工具名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read-file",
                    Arguments = """{"file_path": "/nonexistent/t2.txt"}"""
                }
            }], "T2: 工具名包含连字符 read-file");
        }
        if (lower.Contains("t4") || lower.Contains("markdown result") || lower.Contains("markdown结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '| col1 | col2 |\\n|---|---|\\n| a | b |'"}"""
                }
            }], "T4: 工具结果包含 markdown 表格");
        }
        if (lower.Contains("t5") || lower.Contains("json result") || lower.Contains("json结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '{\"key\":\"value\",\"num\":42}'"}"""
                }
            }], "T5: 工具结果包含 JSON");
        }
        return (null, "");
    }

    /// <summary>toolResultCount == 0 时的处理：工具调用优先，其次 M1 多轮对话记忆</summary>
    private static (InjectDto Response, string Description)? HandleNoToolResult(JsonElement root, string userMessage, string protocol, bool isStream) {
        var (toolCalls, toolDesc) = MatchToolCallIntent(userMessage);
        if (toolCalls is not null)
            return BuildToolCallResponse(toolCalls, toolDesc, userMessage, protocol, isStream);

        var prevAssistant = ExtractPreviousAssistantMessage(root);
        if (prevAssistant is null || prevAssistant.Length == 0) return null;

        var m1Reply = $"我记得你之前说了：{prevAssistant}";
        if (protocol == "anthropic")
            return (new InjectDto { Body = BuildAnthropicResponse(m1Reply) }, "M1: 多轮对话记忆 [anthropic]");
        if (isStream)
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = BuildOpenAiStreamChunks(m1Reply) }, "M1: 多轮对话记忆 [stream]");
        return (new InjectDto { Body = BuildOpenAiResponse(m1Reply) }, "M1: 多轮对话记忆");
    }

    /// <summary>构建工具调用响应（含 S3 content 前缀支持）</summary>
    private static (InjectDto Response, string Description) BuildToolCallResponse(List<ToolCallDto> toolCalls, string toolDesc, string userMessage, string protocol, bool isStream) {
        var lower = userMessage.ToLowerInvariant();
        var s3Content = lower.Contains("s3") || lower.Contains("content and tool") || lower.Contains("文本加工具")
            ? "我先分析一下，然后调用工具。" : null;
        if (protocol == "openai" && isStream)
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = BuildS3StreamChunks(toolCalls, s3Content) }, $"{toolDesc} [stream]");
        return (new InjectDto { Body = BuildOpenAiToolCallResponse(toolCalls, s3Content) }, toolDesc);
    }

    /// <summary>S3: 构建带可选 content 前缀的 tool_call 流式 chunks</summary>
    private static List<string> BuildS3StreamChunks(List<ToolCallDto> toolCalls, string? content) {
        var chunks = BuildOpenAiToolCallStreamChunks(toolCalls);
        if (content is not null) {
            var contentChunks = BuildOpenAiStreamChunks(content);
            chunks.InsertRange(0, contentChunks.GetRange(0, contentChunks.Count - 2));
        }
        return chunks;
    }

    private static string BuildOpenAiToolCallResponse(List<ToolCallDto> toolCalls, string? content = null) {
        var resp = new OpenAiResponse {
            Id = $"chatcmpl-{Guid.NewGuid():N}"[..30],
            Choices = [new OpenAiChoice {
                Index = 0,
                Message = new OpenAiMessage { Content = content, ToolCalls = toolCalls },
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
