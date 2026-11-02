namespace MockAiResponder;

/// <summary>SY1-SY20: 工具调用处理边缘场景 — 测试 jcc 对异常工具调用格式的处理</summary>
internal static partial class IntentMatcher {
    /// <summary>SY1-SY20: 工具调用处理边缘场景匹配</summary>
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSyIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("sy_1\n") || lower.Contains("sy tool name with dot")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "read.file", Arguments = """{"file_path": "/nonexistent/sy1.txt"}""" }
            }], "SY1: 工具名含点号 read.file");
        }

        if (lower.Contains("sy_2\n") || lower.Contains("sy tool name with dash")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "read-file", Arguments = """{"file_path": "/nonexistent/sy2.txt"}""" }
            }], "SY2: 工具名含连字符 read-file");
        }

        if (lower.Contains("sy_3\n") || lower.Contains("sy tool name with slash")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "read/file", Arguments = """{"file_path": "/nonexistent/sy3.txt"}""" }
            }], "SY3: 工具名含斜杠 read/file");
        }

        if (lower.Contains("sy_4\n") || lower.Contains("sy tool name with colon")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "read:file", Arguments = """{"file_path": "/nonexistent/sy4.txt"}""" }
            }], "SY4: 工具名含冒号 read:file");
        }

        if (lower.Contains("sy_5\n") || lower.Contains("sy tool name with at")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "read@file", Arguments = """{"file_path": "/nonexistent/sy5.txt"}""" }
            }], "SY5: 工具名含@ read@file");
        }

        if (lower.Contains("sy_6\n") || lower.Contains("sy missing function field")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "", Arguments = """{"command": "echo sy6"}""" }
            }], "SY6: tool_call 缺少 function.name");
        }

        if (lower.Contains("sy_7\n") || lower.Contains("sy null arguments")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = "null" }
            }], "SY7: 工具参数为 null 字面量");
        }

        if (lower.Contains("sy_8\n") || lower.Contains("sy array arguments")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = "[1, 2, 3]" }
            }], "SY8: 工具参数为数组");
        }

        if (lower.Contains("sy_9\n") || lower.Contains("sy string arguments")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = "\"just a string\"" }
            }], "SY9: 工具参数为字符串");
        }

        if (lower.Contains("sy_10\n") || lower.Contains("sy number arguments")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = "42" }
            }], "SY10: 工具参数为数字");
        }

        if (lower.Contains("sy_11\n") || lower.Contains("sy boolean arguments")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = "true" }
            }], "SY11: 工具参数为布尔值");
        }

        if (lower.Contains("sy_12\n") || lower.Contains("sy deeply nested args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"a":{"b":{"c":{"d":"deep"}}}}""" }
            }], "SY12: 深度嵌套参数（4层）");
        }

        if (lower.Contains("sy_13\n") || lower.Contains("sy array in args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"files": ["a.txt", "b.txt", "c.txt"]}""" }
            }], "SY13: 参数含数组");
        }

        if (lower.Contains("sy_14\n") || lower.Contains("sy mixed types in args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"str": "text", "num": 42, "float": 3.14, "bool": true, "null": null, "arr": [1,2], "obj": {"x":1}}""" }
            }], "SY14: 参数含所有 JSON 类型");
        }

        if (lower.Contains("sy_15\n") || lower.Contains("sy very long tool name")) {
            var longName = new string('a', 200);
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = longName, Arguments = """{"command": "echo sy15"}""" }
            }], "SY15: 超长工具名（200字符）");
        }

        if (lower.Contains("sy_16\n") || lower.Contains("sy very long arguments")) {
            var longArg = new string('x', 50000);
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = $"echo {longArg}" }) }
            }], "SY16: 超长工具参数（50KB）");
        }

        if (lower.Contains("sy_17\n") || lower.Contains("sy unicode in tool name")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "读取文件", Arguments = """{"file_path": "/nonexistent/sy17.txt"}""" }
            }], "SY17: 工具名含中文");
        }

        if (lower.Contains("sy_18\n") || lower.Contains("sy emoji in arguments")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '🎉🚀❤️©™✅❌⚡'"}""" }
            }], "SY18: 参数含 emoji");
        }

        if (lower.Contains("sy_19\n") || lower.Contains("sy surrogate pair in args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '\uD83D\uDE00 \uD83D\uDE80 \u2764\uFE0F'"}""" }
            }], "SY19: 参数含代理对");
        }

        if (lower.Contains("sy_20\n") || lower.Contains("sy control chars in args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'a\\tb\\nc\\rd\\e\\f\\a'"}""" }
            }], "SY20: 参数含控制字符");
        }

        return (null, "");
    }
}
