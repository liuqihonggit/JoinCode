namespace MockAiResponder;

/// <summary>G1-G20: 工具调用参数边缘场景 — 从 IntentMatcher 提取的 partial 方法</summary>
internal static partial class IntentMatcher {
    /// <summary>G1-G20: 工具调用参数边缘场景匹配</summary>
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeGIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("g1\n") || lower.Contains("empty arguments")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = "{}" }
            }], "G1: 工具调用空 arguments");
        }

        if (lower.Contains("g2\n") || lower.Contains("no arguments field")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = null }
            }], "G2: 工具调用无 arguments 字段");
        }

        if (lower.Contains("g3\n") || lower.Contains("duplicate tool id")) {
            var dupId = $"call_{Guid.NewGuid():N}"[..20];
            return ([new ToolCallDto {
                Id = dupId,
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g3_first"}""" }
            }, new ToolCallDto {
                Id = dupId,
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g3_second"}""" }
            }], "G3: 重复 tool_call_id");
        }

        if (lower.Contains("g4\n") || lower.Contains("very long tool name")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = new string('t', 255), Arguments = """{"command": "echo g4"}""" }
            }], "G4: 超长工具名 255 字符");
        }

        if (lower.Contains("g5\n") || lower.Contains("special char id")) {
            return ([new ToolCallDto {
                Id = "call_@#$%^&*()_special",
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g5_special_id"}""" }
            }], "G5: ID 含特殊字符");
        }

        if (lower.Contains("g6\n") || lower.Contains("empty model field")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g6_no_model"}""" }
            }], "G6: 响应缺 model 字段");
        }

        if (lower.Contains("g7\n") || lower.Contains("extra metadata")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g7_extra_meta"}""" }
            }], "G7: 响应含额外 metadata");
        }

        if (lower.Contains("g8\n") || lower.Contains("usage every chunk")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g8_usage_per_chunk"}""" }
            }], "G8: usage 在每个 chunk");
        }

        if (lower.Contains("g9\n") || lower.Contains("cached tokens")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g9_cached"}""" }
            }], "G9: cached_tokens 在 usage");
        }

        if (lower.Contains("g10\n") || lower.Contains("reasoning non stream")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g10_reasoning"}""" }
            }], "G10: reasoning_content 非流式");
        }

        if (lower.Contains("g11\n") || lower.Contains("multi choices non stream")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g11_multi"}""" }
            }], "G11: 多 choices 非流式");
        }

        if (lower.Contains("g12\n") || lower.Contains("markdown link output")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '[g12 link](https://example.com) text'"}""" }
            }], "G12: 工具结果含 markdown 链接");
        }

        if (lower.Contains("g13\n") || lower.Contains("html entities")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'g13 &amp; &lt; &gt; &quot; entities'"}""" }
            }], "G13: 工具结果含 HTML 实体");
        }

        if (lower.Contains("g14\n") || lower.Contains("unicode escape")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'g14 \\u0041\\u0042\\u0043 unicode_escape'"}""" }
            }], "G14: 工具结果含 Unicode 转义");
        }

        if (lower.Contains("g15\n") || lower.Contains("repeated lines")) {
            var cmd = string.Join("; ", Enumerable.Range(0, 100).Select(_ => "echo g15_repeated"));
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = cmd }, JsonOpts) }
            }], "G15: 工具结果含重复行 100 行");
        }

        if (lower.Contains("g16\n") || lower.Contains("progressive output")) {
            var cmd = string.Join("; ", Enumerable.Range(0, 20).Select(i => $"echo g16_progress_{i}"));
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = JsonSerializer.Serialize(new { command = cmd }, JsonOpts) }
            }], "G16: 工具结果含渐进式输出");
        }

        if (lower.Contains("g17\n") || lower.Contains("tool then text no tool")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g17_tool_done"}""" }
            }], "G17: 工具调用后纯文本无工具");
        }

        if (lower.Contains("g18\n") || lower.Contains("5 round tool chain")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g18_round_1"}""" }
            }], "G18: 5 轮工具调用链第1步");
        }

        if (lower.Contains("g19\n") || lower.Contains("nested array 3")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g19", "data": [[[1, 2], [3, 4]], [[5, 6], [7, 8]]]}""" }
            }], "G19: 工具参数含嵌套数组 3 层");
        }

        if (lower.Contains("g20\n") || lower.Contains("null value args")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo g20", "null_val": null, "empty_arr": [], "empty_obj": {}}""" }
            }], "G20: 工具参数含 null 值");
        }

        return (null, "");
    }
}
