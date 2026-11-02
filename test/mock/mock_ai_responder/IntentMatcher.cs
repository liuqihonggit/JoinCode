namespace MockAiResponder;

/// <summary>
/// 意图匹配器 — 解析 jcc 请求，按用户消息内容生成模拟 AI 响应。
/// 支持 OpenAI（流式+非流式）和 Anthropic（非流式）协议。
/// </summary>
internal static partial class IntentMatcher {
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

            var lowerFinal = userMessage.ToLowerInvariant();
            var isC20 = lowerFinal.Contains("c20\n") || lowerFinal.Contains("tool then empty");
            var finalReply = isC20 ? "" : "工具执行完毕。这是模拟 AI 的最终回应。";
            var finalDesc = isC20 ? "C20: 工具结果→空 content" : "工具结果→最终回应";
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
        if (lower.Contains("unicode") || lower.Contains("emoji") || lower.Contains("h4_unicode\n")) {
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
        if (lower.Contains("multi tool") || lower.Contains("多工具") || lower.Contains("f3_multi\n")) {
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
        if (lower.Contains("c19\n") || lower.Contains("tool chain")) {
            if (toolResultCount == 1) {
                return (new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c19_step2_done"}""" }
                }, "C19: 工具链第二步");
            }
        }
        if (lower.Contains("e2\n") || lower.Contains("retry after fail")) {
            if (toolResultCount == 1) {
                return (new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo e2_retry_success"}""" }
                }, "E2: 工具失败后重试（第2次成功）");
            }
        }
        if (lower.Contains("e4\n") || lower.Contains("chain depth 5")) {
            if (toolResultCount is > 0 and < 5) {
                return (new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = $"echo e4_step{toolResultCount + 1}" }, JsonOpts) }
                }, $"E4: 工具链第{toolResultCount + 1}步");
            }
        }
        if (lower.Contains("g18\n") || lower.Contains("5 round tool chain")) {
            if (toolResultCount is > 0 and < 5) {
                return (new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = $"echo g18_round_{toolResultCount + 1}" }, JsonOpts) }
                }, $"G18: 5 轮工具链第{toolResultCount + 1}步");
            }
        }
        if (lower.Contains("i1\n") || lower.Contains("fail then retry")) {
            if (toolResultCount == 1) {
                return (new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo i1_retry_success"}""" }
                }, "I1: 工具失败后重试成功");
            }
        }
        if (lower.Contains("i2\n") || lower.Contains("3 consecutive fails")) {
            if (toolResultCount is > 0 and < 3) {
                return (new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = $"nonexistent_i2_{toolResultCount}", Arguments = "{}" }
                }, $"I2: 第{toolResultCount + 1}次失败");
            }
        }
        return (null, "");
    }
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchToolCallIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();
        if (lower.Contains("read file") || lower.Contains("读文件") || lower.Contains("f1_read\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = """{"file_path": "/nonexistent/missing_file.txt"}"""
                }
            }], "F1: 工具调用失败（read 不存在的文件）");
        }
        if (lower.Contains("multi tool") || lower.Contains("多工具") || lower.Contains("f3_multi\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "read",
                    Arguments = """{"file_path": "/nonexistent/file1.txt"}"""
                }
            }], "F3: 多工具连续调用（第1个）");
        }
        if (lower.Contains("empty tool") || lower.Contains("空工具") || lower.Contains("i_empty\n")) {
            return ([], "I1: 空工具调用（tool_calls=[]）");
        }
        if (lower.Contains("multi call") || lower.Contains("多调用") || lower.Contains("i_multi\n")) {
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
        if (lower.Contains("tool error") || lower.Contains("工具错误") || lower.Contains("i_error\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "exit 1"}"""
                }
            }], "I3: 工具执行错误（bash exit 1）");
        }
        if (lower.Contains("tool timeout") || lower.Contains("工具超时") || lower.Contains("f2_timeout\n")) {
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
        if (lower.Contains("u1") || lower.Contains("no finish") || lower.Contains("无finish")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo u1_done"}"""
                }
            }], "U1: 流式 tool_call 后无 finish_reason");
        }
        if (lower.Contains("u2") || lower.Contains("code block") || lower.Contains("代码块结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '```python\\nprint(\"hello\")\\n```'"}"""
                }
            }], "U2: 工具结果包含代码块");
        }
        if (lower.Contains("u4") || lower.Contains("nested json") || lower.Contains("嵌套json参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo u4", "options": {"verbose": true, "flags": ["a", "b"]}}"""
                }
            }], "U4: 工具参数包含嵌套 JSON");
        }
        if (lower.Contains("v2") || lower.Contains("url result") || lower.Contains("url结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'https://example.com/path?q=1&r=2'"}"""
                }
            }], "V2: 工具结果包含 URL");
        }
        if (lower.Contains("v5") || lower.Contains("null id") || lower.Contains("null的id")) {
            return ([new ToolCallDto {
                Id = "",
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo v5_done"}"""
                }
            }], "V5: 空 tool_call_id");
        }
        if (lower.Contains("w1\n") || lower.Contains("multi chunk") || lower.Contains("多chunk")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo w1_done"}"""
                }
            }], "W1: 多 content chunk 分散");
        }
        if (lower.Contains("w2\n") || lower.Contains("file path") || lower.Contains("文件路径结果")) {
#pragma warning disable JCC1018 // W2 确需 Windows 路径测试
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'C:\\Users\\test\\file.txt'"}"""
                }
            }], "W2: 工具结果包含 Windows 文件路径");
#pragma warning restore JCC1018
        }
        if (lower.Contains("w3\n") || lower.Contains("underscore name") || lower.Contains("下划线工具名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "tool_name_test",
                    Arguments = """{"command": "echo w3"}"""
                }
            }], "W3: 工具名包含下划线");
        }
        if (lower.Contains("w4\n") || lower.Contains("array args") || lower.Contains("数组参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """["a","b","c"]"""
                }
            }], "W4: 工具参数为 JSON 数组");
        }
        if (lower.Contains("w5\n") || lower.Contains("long tool name") || lower.Contains("超长工具名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = new string('t', 200),
                    Arguments = """{"command": "echo w5"}"""
                }
            }], "W5: 超长工具名（200字符）");
        }
        if (lower.Contains("w6\n") || lower.Contains("special id") || lower.Contains("特殊字符id")) {
            return ([new ToolCallDto {
                Id = "call_!@#$%^&*()_+",
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo w6_done"}"""
                }
            }], "W6: tool_call_id 包含特殊字符");
        }
        if (lower.Contains("w7\n") || lower.Contains("empty lines") || lower.Contains("空行结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf '\\n\\n\\n'"}"""
                }
            }], "W7: 工具结果只有空行");
        }
        if (lower.Contains("w8\n") || lower.Contains("float args") || lower.Contains("浮点参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo", "timeout": 3.14, "count": -42}"""
                }
            }], "W8: 工具参数包含浮点数和负数");
        }
        if (lower.Contains("w9\n") || lower.Contains("emoji result") || lower.Contains("emoji结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'Hello 🌍 🚀 ✨ emoji'}"""
                }
            }], "W9: 工具结果包含 emoji");
        }
        if (lower.Contains("w10\n") || lower.Contains("csv result") || lower.Contains("csv结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'a,b,c\\n1,2,3\\n4,5,6'"}"""
                }
            }], "W10: 工具结果包含 CSV");
        }
        if (lower.Contains("w11\n") || lower.Contains("xml result") || lower.Contains("xml结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '<root><item>test</item></root>'"}"""
                }
            }], "W11: 工具结果包含 XML");
        }
        if (lower.Contains("w12\n") || lower.Contains("html result") || lower.Contains("html结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '<html><body><h1>Title</h1></body></html>'"}"""
                }
            }], "W12: 工具结果包含 HTML");
        }
        if (lower.Contains("w13\n") || lower.Contains("empty object") || lower.Contains("空对象参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "{}"
                }
            }], "W13: 工具参数为空对象 {}");
        }
        if (lower.Contains("w14\n") || lower.Contains("only spaces") || lower.Contains("纯空格结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf '   '"}"""
                }
            }], "W14: 工具结果只有空格");
        }
        if (lower.Contains("w15\n") || lower.Contains("crlf") || lower.Contains("回车换行")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf 'line1\\r\\nline2\\r\\n'"}"""
                }
            }], "W15: 工具结果包含 CRLF");
        }
        if (lower.Contains("w16\n") || lower.Contains("deep nested") || lower.Contains("深层嵌套")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo w16", "a": {"b": {"c": {"d": {"e": "deep"}}}}}"""
                }
            }], "W16: 工具参数深层嵌套 JSON");
        }
        if (lower.Contains("w17\n") || lower.Contains("number name") || lower.Contains("数字工具名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "123456",
                    Arguments = """{"command": "echo w17"}"""
                }
            }], "W17: 工具名为纯数字");
        }
        if (lower.Contains("w18\n") || lower.Contains("tsv result") || lower.Contains("tsv结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf 'a\\tb\\tc\\n1\\t2\\t3\\n'"}"""
                }
            }], "W18: 工具结果包含 TSV");
        }
        if (lower.Contains("w19\n") || lower.Contains("bool args") || lower.Contains("布尔参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo w19", "verbose": true, "dry_run": false}"""
                }
            }], "W19: 工具参数包含布尔值");
        }
        if (lower.Contains("w20\n") || lower.Contains("mixed encoding") || lower.Contains("混合编码")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'ASCII 中文 日本語 한국어 🎉 mixed'"}"""
                }
            }], "W20: 工具结果混合编码");
        }
        if (lower.Contains("x10\n") || lower.Contains("control chars") || lower.Contains("控制字符")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf '\\x01\\x02\\x03 control'"}"""
                }
            }], "X10: 工具结果包含控制字符");
        }
        if (lower.Contains("x11\n") || lower.Contains("unicode name") || lower.Contains("unicode工具名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "tööl",
                    Arguments = """{"command": "echo x11"}"""
                }
            }], "X11: 工具名包含 Unicode 变音符号");
        }
        if (lower.Contains("x12\n") || lower.Contains("json string args") || lower.Contains("json字符串参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo x12", "data": "{\"key\":\"value\"}"}"""
                }
            }], "X12: 工具参数包含 JSON 字符串值");
        }
        if (lower.Contains("x13\n") || lower.Contains("very long args") || lower.Contains("超长参数值")) {
            var longVal = new string('a', 50000);
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = JsonSerializer.Serialize(new { command = $"echo {longVal}" })
                }
            }], "X13: 工具参数值超长（50KB）");
        }
        if (lower.Contains("x14\n") || lower.Contains("many keys") || lower.Contains("多键参数")) {
            var bigObj = new Dictionary<string, string>();
            for (var k = 0; k < 100; k++) bigObj[$"key{k}"] = $"value{k}";
            bigObj["command"] = "echo x14";
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = JsonSerializer.Serialize(bigObj)
                }
            }], "X14: 工具参数包含 100 个键");
        }
        if (lower.Contains("x15\n") || lower.Contains("tab in args") || lower.Contains("制表符参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "{\"command\": \"echo\\tx15\"}"
                }
            }], "X15: 工具参数包含制表符");
        }
        if (lower.Contains("x16\n") || lower.Contains("newline in args") || lower.Contains("换行参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "{\"command\": \"echo\\nx16\"}"
                }
            }], "X16: 工具参数包含换行符");
        }
        if (lower.Contains("x17\n") || lower.Contains("slash in args") || lower.Contains("斜杠参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'a/b/c\\d/e/f'"}"""
                }
            }], "X17: 工具参数包含正反斜杠混合");
        }
        if (lower.Contains("x18\n") || lower.Contains("pipe in args") || lower.Contains("管道参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo a | grep a"}"""
                }
            }], "X18: 工具参数包含管道符");
        }
        if (lower.Contains("x19\n") || lower.Contains("semicolon in args") || lower.Contains("分号参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo a; echo b"}"""
                }
            }], "X19: 工具参数包含分号");
        }
        if (lower.Contains("x20\n") || lower.Contains("backtick in args") || lower.Contains("反引号参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo `date`"}"""
                }
            }], "X20: 工具参数包含反引号");
        }
        if (lower.Contains("x1\n") || lower.Contains("base64 result") || lower.Contains("base64结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'SGVsbG8gV29ybGQ='"}"""
                }
            }], "X1: 工具结果包含 base64");
        }
        if (lower.Contains("x2\n") || lower.Contains("space in name") || lower.Contains("空格在名中")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "tool name",
                    Arguments = """{"command": "echo x2"}"""
                }
            }], "X2: 工具名中间有空格");
        }
        if (lower.Contains("x3\n") || lower.Contains("number args") || lower.Contains("数字参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "12345"
                }
            }], "X3: 工具参数为纯数字");
        }
        if (lower.Contains("x4\n") || lower.Contains("string args") || lower.Contains("字符串参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "\"hello world\""
                }
            }], "X4: 工具参数为纯字符串");
        }
        if (lower.Contains("x5\n") || lower.Contains("chinese name") || lower.Contains("中文工具名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "工具",
                    Arguments = """{"command": "echo x5"}"""
                }
            }], "X5: 工具名为中文");
        }
        if (lower.Contains("x6\n") || lower.Contains("chinese key") || lower.Contains("中文键参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"命令": "echo x6"}"""
                }
            }], "X6: 工具参数包含中文键");
        }
        if (lower.Contains("x7\n") || lower.Contains("sql result") || lower.Contains("sql结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'SELECT * FROM users WHERE id = 1; DROP TABLE users;'"}"""
                }
            }], "X7: 工具结果包含 SQL 注入");
        }
        if (lower.Contains("x8\n") || lower.Contains("regex result") || lower.Contains("正则结果")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '^[a-zA-Z0-9]+@[a-zA-Z0-9]+\\\\.[a-zA-Z]{2,}$'"}"""
                }
            }], "X8: 工具结果包含正则表达式");
        }
        if (lower.Contains("x9\n") || lower.Contains("datetime args") || lower.Contains("日期参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo x9", "date": "2026-11-02T13:45:00Z"}"""
                }
            }], "X9: 工具参数包含 ISO 8601 日期");
        }
        if (lower.Contains("y1\n") || lower.Contains("parallel calls") || lower.Contains("并行调用")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo y1a"}"""
                }
            }, new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo y1b"}"""
                }
            }], "Y1: 单次响应包含多个并行工具调用");
        }
        if (lower.Contains("y2\n") || lower.Contains("missing args field") || lower.Contains("缺失arguments字段")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = null
                }
            }], "Y2: 工具调用 arguments 字段缺失（null）");
        }
        if (lower.Contains("y3\n") || lower.Contains("empty function name") || lower.Contains("空函数名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "",
                    Arguments = """{"command": "echo y3"}"""
                }
            }], "Y3: 工具调用函数名为空字符串");
        }
        if (lower.Contains("y4\n") || lower.Contains("missing id") || lower.Contains("缺失id")) {
            return ([new ToolCallDto {
                Id = "",
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo y4"}"""
                }
            }], "Y4: 工具调用 ID 为空字符串");
        }
        if (lower.Contains("y5\n") || lower.Contains("null content") || lower.Contains("null内容")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo y5"}"""
                }
            }], "Y5: 响应 content 为 null（工具调用本身）");
        }
        if (lower.Contains("y6\n") || lower.Contains("null in args") || lower.Contains("参数含null")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": null}"""
                }
            }], "Y6: 工具参数 command 值为 null");
        }
        if (lower.Contains("y7\n") || lower.Contains("empty string param") || lower.Contains("空字符串参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": ""}"""
                }
            }], "Y7: 工具参数 command 为空字符串");
        }
        if (lower.Contains("y8\n") || lower.Contains("number for string param") || lower.Contains("数字代替字符串")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": 12345}"""
                }
            }], "Y8: 工具参数 command 为数字而非字符串");
        }
        if (lower.Contains("y9\n") || lower.Contains("boolean for string param") || lower.Contains("布尔代替字符串")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": true}"""
                }
            }], "Y9: 工具参数 command 为布尔值");
        }
        if (lower.Contains("y10\n") || lower.Contains("array for string param") || lower.Contains("数组代替字符串")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": ["echo", "y10"]}"""
                }
            }], "Y10: 工具参数 command 为数组");
        }
        if (lower.Contains("y11\n") || lower.Contains("object for string param") || lower.Contains("对象代替字符串")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": {"nested": "value"}}"""
                }
            }], "Y11: 工具参数 command 为对象");
        }
        if (lower.Contains("y12\n") || lower.Contains("whitespace only args") || lower.Contains("纯空白参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "   "
                }
            }], "Y12: 工具参数为纯空白字符");
        }
        if (lower.Contains("y13\n") || lower.Contains("args as array") || lower.Contains("参数为数组")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "[]"
                }
            }], "Y13: 工具参数为 JSON 空数组");
        }
        if (lower.Contains("y14\n") || lower.Contains("args as boolean") || lower.Contains("参数为布尔")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "true"
                }
            }], "Y14: 工具参数为 JSON 布尔值 true");
        }
        if (lower.Contains("y15\n") || lower.Contains("args as null") || lower.Contains("参数为null")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "null"
                }
            }], "Y15: 工具参数为 JSON null");
        }
        if (lower.Contains("y16\n") || lower.Contains("long id") || lower.Contains("超长id")) {
            var longId = new string('a', 1000);
            return ([new ToolCallDto {
                Id = longId,
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo y16"}"""
                }
            }], "Y16: 工具调用 ID 超长（1000字符）");
        }
        if (lower.Contains("y17\n") || lower.Contains("id with spaces") || lower.Contains("id含空格")) {
            return ([new ToolCallDto {
                Id = "call with spaces and quotes",
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo y17"}"""
                }
            }], "Y17: 工具调用 ID 包含空格和引号");
        }
        if (lower.Contains("y18\n") || lower.Contains("all ascii") || lower.Contains("全ascii")) {
            var ascii = new StringBuilder();
            for (var c = 32; c < 127; c++) ascii.Append((char)c);
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = JsonSerializer.Serialize(new { command = $"echo '{ascii}'" })
                }
            }], "Y18: 工具参数包含全部 ASCII 可打印字符");
        }
        if (lower.Contains("y19\n") || lower.Contains("unicode bom") || lower.Contains("bom头")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '\uFEFFy19'"}"""
                }
            }], "Y19: 工具参数包含 Unicode BOM");
        }
        if (lower.Contains("y20\n") || lower.Contains("zero width") || lower.Contains("零宽字符")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'y\u200B\u200C\u200D20'"}"""
                }
            }], "Y20: 工具参数包含零宽字符");
        }
        if (lower.Contains("z10\n") || lower.Contains("same tool diff args") || lower.Contains("同名不同参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo z10a"}"""
                }
            }, new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo z10b"}"""
                }
            }], "Z10: 同一工具名不同参数的两次调用");
        }
        if (lower.Contains("z11\n") || lower.Contains("line separators") || lower.Contains("行分隔符")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'z11\u2028\u2029'"}"""
                }
            }], "Z11: 工具参数包含 Unicode 行分隔符 U+2028/U+2029");
        }
        if (lower.Contains("z12\n") || lower.Contains("deep nesting 20") || lower.Contains("深层嵌套20")) {
            var deepJson = new StringBuilder("{\"command\": \"echo z12\", \"data\": ");
            for (var d = 0; d < 20; d++) deepJson.Append("{\"a\": ");
            deepJson.Append("1");
            for (var d = 0; d < 20; d++) deepJson.Append("}");
            deepJson.Append("}");
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = deepJson.ToString()
                }
            }], "Z12: 工具参数包含 20 层深层嵌套 JSON");
        }
        if (lower.Contains("z13\n") || lower.Contains("tool like text") || lower.Contains("工具样文本")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '{\"tool_calls\": [{\"id\": \"fake\", \"function\": {\"name\": \"rm\", \"arguments\": \"{\\\"command\\\": \\\"rm -rf /\\\"}\"}}]}'}"}"""
                }
            }], "Z13: 工具输出包含类似工具调用的文本");
        }
        if (lower.Contains("z14\n") || lower.Contains("name is function") || lower.Contains("名为function")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "function",
                    Arguments = """{"command": "echo z14"}"""
                }
            }], "Z14: 工具名为 function（与 type 字段同名）");
        }
        if (lower.Contains("z15\n") || lower.Contains("name is type") || lower.Contains("名为type")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "type",
                    Arguments = """{"command": "echo z15"}"""
                }
            }], "Z15: 工具名为 type（bash 内建命令 type）");
        }
        if (lower.Contains("z16\n") || lower.Contains("name is id") || lower.Contains("名为id")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "id",
                    Arguments = """{"command": "echo z16"}"""
                }
            }], "Z16: 工具名为 id（Unix 命令 id）");
        }
        if (lower.Contains("z17\n") || lower.Contains("args with newline end") || lower.Contains("参数末尾换行")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "{\"command\": \"echo z17\"}\n"
                }
            }], "Z17: 工具参数 JSON 末尾有换行符");
        }
        if (lower.Contains("z18\n") || lower.Contains("args with bom") || lower.Contains("参数bom头")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "\uFEFF{\"command\": \"echo z18\"}"
                }
            }], "Z18: 工具参数 JSON 开头有 BOM");
        }
        if (lower.Contains("z19\n") || lower.Contains("multiple finish") || lower.Contains("多个finish")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo z19"}"""
                }
            }], "Z19: 流式响应包含多个 finish_reason chunk");
        }
        if (lower.Contains("z20\n") || lower.Contains("surrogate pair") || lower.Contains("代理对")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '\uD83D\uDE00\uD83C\uDF89 z20'"}"""
                }
            }], "Z20: 工具参数包含 Unicode 代理对（emoji）");
        }
        if (lower.Contains("z1\n") || lower.Contains("empty tool calls array") || lower.Contains("空工具调用数组")) {
            return ([], "Z1: 响应包含空 tool_calls 数组");
        }
        if (lower.Contains("z2\n") || lower.Contains("duplicate ids") || lower.Contains("重复id")) {
            var dupId = $"call_{Guid.NewGuid():N}"[..20];
            return ([new ToolCallDto {
                Id = dupId,
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo z2a"}"""
                }
            }, new ToolCallDto {
                Id = dupId,
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo z2b"}"""
                }
            }], "Z2: 两个工具调用使用相同 ID");
        }
        if (lower.Contains("z3\n") || lower.Contains("whitespace name") || lower.Contains("纯空白名")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "   ",
                    Arguments = """{"command": "echo z3"}"""
                }
            }], "Z3: 工具名为纯空白字符");
        }
        if (lower.Contains("z4\n") || lower.Contains("malformed json args") || lower.Contains("畸形json参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "{\"command\": \"echo z4"
                }
            }], "Z4: 工具参数为畸形 JSON（缺少闭合引号和括号）");
        }
        if (lower.Contains("z5\n") || lower.Contains("empty object args") || lower.Contains("空对象参数")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "{}"
                }
            }], "Z5: 工具参数为空 JSON 对象");
        }
        if (lower.Contains("z6\n") || lower.Contains("escaped quotes") || lower.Contains("转义引号")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo \"hello \\\"world\\\"\""}"""
                }
            }], "Z6: 工具参数包含多层转义引号");
        }
        if (lower.Contains("z7\n") || lower.Contains("4byte unicode") || lower.Contains("四字节unicode")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo '𝓗𝓮𝓵𝓵𝓸 🅰🅱🅲 z7'"}"""
                }
            }], "Z7: 工具参数包含 4 字节 Unicode 字符");
        }
        if (lower.Contains("z8\n") || lower.Contains("large output") || lower.Contains("大输出")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "yes z8 | head -c 100000"}"""
                }
            }], "Z8: 工具产生 100KB 输出");
        }
        if (lower.Contains("z9\n") || lower.Contains("whitespace output") || lower.Contains("纯空白输出")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf '   \n\t  \n'"}"""
                }
            }], "Z9: 工具输出为纯空白字符");
        }
        if (lower.Contains("a1\n") || lower.Contains("large arg value") || lower.Contains("大参数值")) {
            var largeVal = new string('x', 100000);
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = JsonSerializer.Serialize(new { command = $"echo {largeVal}" })
                }
            }], "A1: 工具参数值 100KB");
        }
        if (lower.Contains("a2\n") || lower.Contains("name with newline") || lower.Contains("名含换行")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash\n",
                    Arguments = """{"command": "echo a2"}"""
                }
            }], "A2: 工具名包含换行符");
        }
        if (lower.Contains("a3\n") || lower.Contains("id with newline") || lower.Contains("id含换行")) {
            return ([new ToolCallDto {
                Id = "call\nwith\nnewline",
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo a3"}"""
                }
            }], "A3: 工具 ID 包含换行符");
        }
        if (lower.Contains("a4\n") || lower.Contains("null bytes content") || lower.Contains("null字节内容")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf 'a\\x00b'"}"""
                }
            }], "A4: 工具输出包含 null 字节");
        }
        if (lower.Contains("a5\n") || lower.Contains("crlf only") || lower.Contains("纯crlf")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "printf '\\r\\n\\r\\n'"}"""
                }
            }], "A5: 工具输出为纯 CRLF");
        }
        if (lower.Contains("a6\n") || lower.Contains("first fails second succeeds") || lower.Contains("首败次成")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "nonexistent_tool",
                    Arguments = "{}"
                }
            }, new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo a6"}"""
                }
            }], "A6: 第一个工具失败第二个成功");
        }
        if (lower.Contains("a7\n") || lower.Contains("key with spaces") || lower.Contains("键含空格")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo a7", "key with spaces": "value"}"""
                }
            }], "A7: 工具参数 JSON 键包含空格");
        }
        if (lower.Contains("a8\n") || lower.Contains("uppercase bash") || lower.Contains("大写bash")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "BASH",
                    Arguments = """{"command": "echo a8"}"""
                }
            }], "A8: 工具名大写 BASH");
        }
        if (lower.Contains("a9\n") || lower.Contains("nested quotes") || lower.Contains("嵌套引号")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "{\"command\": \"echo \\\"hello 'world' \\\"nested\\\"\\\"\"}"
                }
            }], "A9: 工具参数包含多层嵌套引号");
        }
        if (lower.Contains("a10\n") || lower.Contains("long content") || lower.Contains("超长内容")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "yes a10 | head -c 100000"}"""
                }
            }], "A10: 工具产生 100KB 文本输出");
        }
        if (lower.Contains("a11\n") || lower.Contains("id only numbers") || lower.Contains("纯数字id")) {
            return ([new ToolCallDto {
                Id = "12345678901234567890",
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo a11"}"""
                }
            }], "A11: 工具 ID 为纯数字");
        }
        if (lower.Contains("a12\n") || lower.Contains("id unicode") || lower.Contains("unicode id")) {
            return ([new ToolCallDto {
                Id = "call_你好世界_🎉",
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo a12"}"""
                }
            }], "A12: 工具 ID 包含 Unicode 字符");
        }
        if (lower.Contains("a13\n") || lower.Contains("name with dots") || lower.Contains("名含点号")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash.bash",
                    Arguments = """{"command": "echo a13"}"""
                }
            }], "A13: 工具名包含点号");
        }
        if (lower.Contains("a14\n") || lower.Contains("deep array") || lower.Contains("深层数组")) {
            var deepArr = new StringBuilder("{\"command\": \"echo a14\", \"data\": ");
            for (var d = 0; d < 20; d++) deepArr.Append("[");
            deepArr.Append("1");
            for (var d = 0; d < 20; d++) deepArr.Append("]");
            deepArr.Append("}");
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = deepArr.ToString() }
            }], "A14: 工具参数包含 20 层深层嵌套数组");
        }
        if (lower.Contains("a15\n") || lower.Contains("duplicate keys") || lower.Contains("重复键")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo a15a", "command": "echo a15b"}"""
                }
            }], "A15: 工具参数 JSON 包含重复键");
        }
        if (lower.Contains("a16\n") || lower.Contains("mixed quotes") || lower.Contains("混合引号")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo 'hello \"world\" a16'"}"""
                }
            }], "A16: 工具参数包含混合引号");
        }
        if (lower.Contains("a17\n") || lower.Contains("control in args") || lower.Contains("参数含控制符")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = "{\"command\": \"echo 'a17\\t\\r\\n'\"}"
                }
            }], "A17: 工具参数包含制表符/回车/换行");
        }
        if (lower.Contains("a18\n") || lower.Contains("empty command value") || lower.Contains("空命令值")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "", "extra": "value"}"""
                }
            }], "A18: 工具参数 command 为空字符串但有额外参数");
        }
        if (lower.Contains("a19\n") || lower.Contains("very long name") || lower.Contains("超长工具名")) {
            var longName = new string('a', 255);
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = longName,
                    Arguments = """{"command": "echo a19"}"""
                }
            }], "A19: 工具名超长（255 字符）");
        }
        if (lower.Contains("a20\n") || lower.Contains("json with comments") || lower.Contains("json含注释")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto {
                    Name = "bash",
                    Arguments = """{"command": "echo a20" /* comment */}"""
                }
            }], "A20: 工具参数 JSON 包含注释");
        }
        if (lower.Contains("c1\n") || lower.Contains("c1 exit code 1")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c1_stdout; exit 1"}""" }
            }], "C1: 工具返回 exit code 1");
        }
        if (lower.Contains("c2\n") || lower.Contains("c2 exit code 127")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "nonexistent_command_xyz"}""" }
            }], "C2: 工具返回 exit code 127");
        }
        if (lower.Contains("c3\n") || lower.Contains("c3 exit code 255")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "exit 255"}""" }
            }], "C3: 工具返回 exit code 255");
        }
        if (lower.Contains("c4\n") || lower.Contains("stdout and stderr")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c4_stdout; echo c4_stderr >&2"}""" }
            }], "C4: 工具同时输出 stdout 和 stderr");
        }
        if (lower.Contains("c5\n") || lower.Contains("ansi output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\033[31mred\\033[0m text'"}""" }
            }], "C5: 工具输出包含 ANSI 转义序列");
        }
        if (lower.Contains("c6\n") || lower.Contains("json output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '{\"key\":\"value\",\"num\":42}'"}""" }
            }], "C6: 工具输出 JSON 格式数据");
        }
        if (lower.Contains("c7\n") || lower.Contains("markdown output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '# Heading\n\n- item1\n- item2\n\n**bold** text'"}""" }
            }], "C7: 工具输出 markdown 格式");
        }
        if (lower.Contains("c8\n") || lower.Contains("code block output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '```python\nprint(\"hello\")\n```'"}""" }
            }], "C8: 工具输出代码块");
        }
        if (lower.Contains("c9\n") || lower.Contains("empty output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf ''"}""" }
            }], "C9: 工具输出为空字符串");
        }
        if (lower.Contains("c10\n") || lower.Contains("newline only")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\n'"}""" }
            }], "C10: 工具输出只有一个换行符");
        }
        if (lower.Contains("c11\n") || lower.Contains("spaces only")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '   '"}""" }
            }], "C11: 工具输出只有空格");
        }
        if (lower.Contains("c12\n") || lower.Contains("nested json args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c12", "options": {"verbose": true, "count": 3}}""" }
            }], "C12: 工具参数包含嵌套 JSON 对象");
        }
        if (lower.Contains("c13\n") || lower.Contains("array args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c13", "files": ["a.txt", "b.txt", "c.txt"]}""" }
            }], "C13: 工具参数包含 JSON 数组");
        }
        if (lower.Contains("c14\n") || lower.Contains("mixed type args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c14", "count": 42, "verbose": true, "name": "test", "ratio": 3.14, "tags": ["a","b"], "config": {"x":1}}""" }
            }], "C14: 工具参数包含混合类型");
        }
        if (lower.Contains("c15\n") || lower.Contains("parallel success fail")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c15_ok"}""" }
            }, new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "nonexistent_tool", Arguments = "{}" }
            }], "C15: 并行调用一个成功一个失败");
        }
        if (lower.Contains("c16\n") || lower.Contains("parallel all success")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c16_a"}""" }
            }, new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c16_b"}""" }
            }, new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c16_c"}""" }
            }], "C16: 三个并行调用全部成功");
        }
        if (lower.Contains("c17\n") || lower.Contains("parallel all fail")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "nonexistent_a", Arguments = "{}" }
            }, new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "nonexistent_b", Arguments = "{}" }
            }, new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "nonexistent_c", Arguments = "{}" }
            }], "C17: 三个并行调用全部失败");
        }
        if (lower.Contains("c18\n") || lower.Contains("tool then final")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c18_done"}""" }
            }], "C18: 工具调用后最终回应");
        }
        if (lower.Contains("c19\n") || lower.Contains("tool chain")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c19_step1"}""" }
            }], "C19: 工具链第一步");
        }
        if (lower.Contains("c20\n") || lower.Contains("tool then empty")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo c20_done"}""" }
            }], "C20: 工具调用后空 content");
        }

        var eResult = MatchEdgeEIntent(userMessage);
        if (eResult.ToolCalls is not null)
            return eResult;

        var fResult = MatchEdgeFIntent(userMessage);
        if (fResult.ToolCalls is not null)
            return fResult;

        var gResult = MatchEdgeGIntent(userMessage);
        if (gResult.ToolCalls is not null)
            return gResult;

        var iResult = MatchEdgeIIntent(userMessage);
        if (iResult.ToolCalls is not null)
            return iResult;

        return (null, "");
    }

    /// <summary>toolResultCount == 0 时的处理：工具调用优先，其次 M1 多轮对话记忆</summary>
    private static (InjectDto Response, string Description)? HandleNoToolResult(JsonElement root, string userMessage, string protocol, bool isStream) {
        var (toolCalls, toolDesc) = MatchToolCallIntent(userMessage);
        if (toolCalls is not null)
            return BuildToolCallResponse(toolCalls, toolDesc, userMessage, protocol, isStream);

        if (protocol == "openai" && isStream) {
            var dResult = MatchStreamingEdgeIntent(userMessage);
            if (dResult is not null)
                return dResult;
        }

        var hResult = MatchEdgeHIntent(userMessage, protocol, isStream);
        if (hResult is not null)
            return hResult;

        var prevAssistant = ExtractPreviousAssistantMessage(root);
        if (prevAssistant is null || prevAssistant.Length == 0) return null;

        var m1Reply = $"我记得你之前说了：{prevAssistant}";
        if (protocol == "anthropic")
            return (new InjectDto { Body = BuildAnthropicResponse(m1Reply) }, "M1: 多轮对话记忆 [anthropic]");
        if (isStream)
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = BuildOpenAiStreamChunks(m1Reply) }, "M1: 多轮对话记忆 [stream]");
        return (new InjectDto { Body = BuildOpenAiResponse(m1Reply) }, "M1: 多轮对话记忆");
    }

    /// <summary>构建工具调用响应（含 S3 content 前缀 + U1 无 finish_reason 支持）</summary>
    private static (InjectDto Response, string Description) BuildToolCallResponse(List<ToolCallDto> toolCalls, string toolDesc, string userMessage, string protocol, bool isStream) {
        var lower = userMessage.ToLowerInvariant();
        var s3Content = lower.Contains("s3") || lower.Contains("content and tool") || lower.Contains("文本加工具")
            ? "我先分析一下，然后调用工具。" : null;
        if (protocol == "openai" && isStream) {
            var chunks = BuildS3StreamChunks(toolCalls, s3Content);
            if (lower.Contains("u1") || lower.Contains("no finish") || lower.Contains("无finish"))
                chunks.RemoveAt(chunks.Count - 2);
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, $"{toolDesc} [stream]");
        }
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
