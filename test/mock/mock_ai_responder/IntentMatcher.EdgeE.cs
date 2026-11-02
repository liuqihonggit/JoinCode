namespace MockAiResponder;

/// <summary>E1-E20: 工具调用边缘场景 — 从 IntentMatcher 提取的 partial 方法</summary>
internal static partial class IntentMatcher {
    /// <summary>E1-E20: 工具调用边缘场景匹配</summary>
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeEIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("e1\n") || lower.Contains("parallel fail success")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo e1_ok"}""" }
            }, new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "nonexistent_tool", Arguments = "{}" }
            }], "E1: 并行调用一个成功一个失败");
        }

        if (lower.Contains("e2\n") || lower.Contains("retry after fail")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "nonexistent_cmd_xyz"}""" }
            }], "E2: 工具失败后 LLM 重试");
        }

        if (lower.Contains("e3\n") || lower.Contains("five parallel")) {
            return (Enumerable.Range(0, 5).Select(i => new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = $"echo e3_{i}" }, JsonOpts) }
            }).ToList(), "E3: 5 个并行工具调用");
        }

        if (lower.Contains("e4\n") || lower.Contains("chain depth 5")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo e4_step1"}""" }
            }], "E4: 工具链 5 层深度第一步");
        }

        if (lower.Contains("e5\n") || lower.Contains("missing quotes")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{command: "echo e5"}""" }
            }], "E5: 工具参数 JSON 缺少引号");
        }

        if (lower.Contains("e6\n") || lower.Contains("trailing comma")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo e6",}""" }
            }], "E6: 工具参数 JSON 多余逗号");
        }

        if (lower.Contains("e7\n") || lower.Contains("single quote")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = "{'command': 'echo e7'}" }
            }], "E7: 工具参数 JSON 单引号");
        }

        if (lower.Contains("e8\n") || lower.Contains("json comment")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo e8" // comment}""" }
            }], "E8: 工具参数 JSON 含注释");
        }

        if (lower.Contains("e9\n") || lower.Contains("100 lines output")) {
            var cmd = string.Join("; ", Enumerable.Range(0, 100).Select(i => $"echo line_{i}"));
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = cmd }, JsonOpts) }
            }], "E9: 工具输出 100 行");
        }

        if (lower.Contains("e10\n") || lower.Contains("json output tool")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '{\"name\":\"e10\",\"value\":42,\"nested\":{\"a\":1,\"b\":[2,3]}}'"}""" }
            }], "E10: 工具输出 JSON");
        }

        if (lower.Contains("e11\n") || lower.Contains("markdown table")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '| col1 | col2 |\\n|------|------|\\n| a | b |\\n| c | d |'"}""" }
            }], "E11: 工具输出 markdown 表格");
        }

        if (lower.Contains("e12\n") || lower.Contains("code block output tool")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '```python\\ndef hello():\\n    print(\"e12\")\\n```'"}""" }
            }], "E12: 工具输出代码块");
        }

        if (lower.Contains("e13\n") || lower.Contains("empty history")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo e13_first_message"}""" }
            }], "E13: 空对话历史首次消息");
        }

        if (lower.Contains("e14\n") || lower.Contains("long system prompt")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo e14_long_prompt_handled"}""" }
            }], "E14: 超长系统提示下工具调用");
        }

        if (lower.Contains("e15\n") || lower.Contains("emoji args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '🔧🚀⚡ e15_emoji_test'}""" }
            }], "E15: 工具参数包含 emoji");
        }

        if (lower.Contains("e16\n") || lower.Contains("rtl text")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'مرحبا بالعالم e16'}""" }
            }], "E16: 工具参数包含 RTL 文本");
        }

        if (lower.Contains("e17\n") || lower.Contains("very long id")) {
            return ([new ToolCallDto {
                Id = new string('a', 1000),
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo e17_long_id"}""" }
            }], "E17: 工具调用 ID 超长 1000 字符");
        }

        if (lower.Contains("e18\n") || lower.Contains("ten tool calls")) {
            return (Enumerable.Range(0, 10).Select(i => new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = $"echo e18_{i}" }, JsonOpts) }
            }).ToList(), "E18: 一轮 10 个工具调用");
        }

        if (lower.Contains("e19\n") || lower.Contains("null byte output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'e19_before\\x00e19_after'"}""" }
            }], "E19: 工具输出包含 null 字节");
        }

        if (lower.Contains("e20\n") || lower.Contains("control chars output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'e20_\\x01\\x02\\x03ctrl'"}""" }
            }], "E20: 工具输出包含控制字符");
        }

        return (null, "");
    }
}
