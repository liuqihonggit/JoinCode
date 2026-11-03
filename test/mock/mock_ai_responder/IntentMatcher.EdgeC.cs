namespace MockAiResponder;

/// <summary>C1-C20: 工具调用场景 — 退出码/输出格式/并行调用</summary>
internal static partial class IntentMatcher {
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeCIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

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

        return (null, "");
    }
}
