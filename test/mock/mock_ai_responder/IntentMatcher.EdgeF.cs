namespace MockAiResponder;

/// <summary>F1-F20: 工具调用高级边缘场景 — 从 IntentMatcher 提取的 partial 方法</summary>
internal static partial class IntentMatcher {
    /// <summary>F1-F20: 工具调用高级边缘场景匹配</summary>
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeFIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("f1\n") || lower.Contains("10kb single line")) {
            var longText = new string('x', 10240);
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = $"echo {longText}" }, JsonOpts) }
            }], "F1: 工具结果含 10KB 单行");
        }

        if (lower.Contains("f2\n") || lower.Contains("1000 lines")) {
            var cmd = string.Join("; ", Enumerable.Range(0, 1000).Select(i => $"echo line_{i}"));
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = cmd }, JsonOpts) }
            }], "F2: 工具结果含 1000 行");
        }

        if (lower.Contains("f3\n") || lower.Contains("crlf output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'f3_line1\\r\\nf3_line2\\r\\n'"}""" }
            }], "F3: 工具结果含 CRLF Windows 换行");
        }

        if (lower.Contains("f4\n") || lower.Contains("bom output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\xef\\xbb\\xbxf4_bom_text'"}""" }
            }], "F4: 工具结果含 UTF-8 BOM 头");
        }

        if (lower.Contains("f5\n") || lower.Contains("nested json 5")) {
            var nested = """{"command": "echo f5", "config": {"level1": {"level2": {"level3": {"level4": {"level5": "deep"}}}}}}""";
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = nested }
            }], "F5: 工具参数含嵌套 JSON 5 层");
        }

        if (lower.Contains("f6\n") || lower.Contains("100 keys args")) {
            var jsonObj = new System.Text.Json.Nodes.JsonObject { ["command"] = "echo f6" };
            for (var i = 0; i < 100; i++) jsonObj[$"key_{i}"] = i;
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = jsonObj.ToJsonString() }
            }], "F6: 工具参数含 100 个键");
        }

        if (lower.Contains("f7\n") || lower.Contains("all json types")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo f7", "str": "text", "int": 42, "float": 3.14, "bool": true, "null": null, "arr": [1,2,3], "obj": {"a":1}}""" }
            }], "F7: 工具参数含所有 JSON 类型");
        }

        if (lower.Contains("f8\n") || lower.Contains("3 rounds 3 tools")) {
            return (Enumerable.Range(0, 3).Select(i => new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = $"echo f8_round_{i}" }, JsonOpts) }
            }).ToList(), "F8: 一轮 3 个工具调用（多轮多工具）");
        }

        if (lower.Contains("f9\n") || lower.Contains("empty then tool")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo f9_after_empty"}""" }
            }], "F9: 空 content 后工具调用");
        }

        if (lower.Contains("f10\n") || lower.Contains("tool name digits")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash123", Arguments = """{"command": "echo f10"}""" }
            }], "F10: 工具名含数字");
        }

        if (lower.Contains("f11\n") || lower.Contains("tool name underscore")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "my_bash_tool", Arguments = """{"command": "echo f11"}""" }
            }], "F11: 工具名含下划线");
        }

        if (lower.Contains("f12\n") || lower.Contains("tool name mixed case")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "Bash", Arguments = """{"command": "echo f12"}""" }
            }], "F12: 工具名混合大小写");
        }

        if (lower.Contains("f13\n") || lower.Contains("4 byte emoji args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '𝕏𝕐𝕏 f13_4byte_emoji'"}""" }
            }], "F13: 工具参数含 4 字节 emoji（补充平面）");
        }

        if (lower.Contains("f14\n") || lower.Contains("ansi color output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\033[31mred\\033[32mgreen\\033[33myellow\\033[0m f14'"}""" }
            }], "F14: 工具结果含 ANSI 颜色码");
        }

        if (lower.Contains("f15\n") || lower.Contains("terminal control")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\033[2K\\033[1Gf15_cleared'"}""" }
            }], "F15: 工具结果含终端控制码");
        }

        if (lower.Contains("f16\n") || lower.Contains("chinese args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '你好世界 f16_中文参数'"}""" }
            }], "F16: 工具参数含中文");
        }

        if (lower.Contains("f17\n") || lower.Contains("japanese args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'こんにちは世界 f17_日本語'"}""" }
            }], "F17: 工具参数含日文");
        }

        if (lower.Contains("f18\n") || lower.Contains("korean args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '안녕하세요 f18_한국어'"}""" }
            }], "F18: 工具参数含韩文");
        }

        if (lower.Contains("f19\n") || lower.Contains("cyrillic args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'Привет мир f19_Кириллица'"}""" }
            }], "F19: 工具参数含西里尔文");
        }

        if (lower.Contains("f20\n") || lower.Contains("mixed script")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'Hello 世界 こんにちは 안녕 Привет f20_mixed'"}""" }
            }], "F20: 工具参数含混合脚本");
        }

        return (null, "");
    }
}
