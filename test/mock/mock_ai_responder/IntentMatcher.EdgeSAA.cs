namespace MockAiResponder;

/// <summary>SAA1-SAA20: 深层边缘场景 — 测试工具调用链路的不舒服点</summary>
internal static partial class IntentMatcher {
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSAAIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("saa_1\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo saa1_result"}""" }
            }], "SAA1: 工具调用后正常响应");
        }

        if (lower.Contains("saa_2\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'saa2'}""" }
            }], "SAA2: 简单工具调用");
        }

        if (lower.Contains("saa_3\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo $HOME"}""" }
            }], "SAA3: 工具返回包含路径");
        }

        if (lower.Contains("saa_4\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sk-1234567890abcdef'"}""" }
            }], "SAA4: 工具返回包含 API key 格式");
        }

        if (lower.Contains("saa_5\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'saa5_line1'; echo 'saa5_line2'; echo 'saa5_line3'"}""" }
            }], "SAA5: 多行工具输出");
        }

        if (lower.Contains("saa_6\n")) {
            return ([new ToolCallDto {
                Id = "mismatched_id_saa6",
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo saa6"}""" }
            }], "SAA6: 固定 tool_call_id");
        }

        if (lower.Contains("saa_7\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo saa7_a"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo saa7_b"}""" }
                }
            ], "SAA7: 并发两个工具调用");
        }

        if (lower.Contains("saa_8\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'saa8 with spaces and special chars: !@#$%^&*()'}""" }
            }], "SAA8: 工具参数特殊字符");
        }

        if (lower.Contains("saa_9\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": ""}""" }
            }], "SAA9: 空命令");
        }

        if (lower.Contains("saa_10\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '{\"nested\":{\"deep\":{\"value\":42}}}'"}""" }
            }], "SAA10: 工具返回嵌套 JSON");
        }

        if (lower.Contains("saa_11\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'saa11_%d ' $(seq 1 100)"}""" }
            }], "SAA11: 工具返回超长单行");
        }

        if (lower.Contains("saa_12\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\x01\\x02\\x03 saa12 control chars'}""" }
            }], "SAA12: 工具返回控制字符");
        }

        if (lower.Contains("saa_13\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'saa13 中文 日本語 한국어 emoji 😀'}""" }
            }], "SAA13: 工具返回多语言+emoji");
        }

        if (lower.Contains("saa_14\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '```python\\nprint(\"saa14\")\\n```'"}""" }
            }], "SAA14: 工具返回 markdown 代码块");
        }

        if (lower.Contains("saa_15\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "seq 1 100 | head -20"}""" }
            }], "SAA15: 工具返回数字序列");
        }

        if (lower.Contains("saa_16\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo saa16 && exit 0"}""" }
            }], "SAA16: 工具成功执行");
        }

        if (lower.Contains("saa_17\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo saa17_error >&2 && exit 1"}""" }
            }], "SAA17: 工具 stderr 输出 + exit 1");
        }

        if (lower.Contains("saa_18\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "sleep 1 && echo saa18_done"}""" }
            }], "SAA18: 工具执行 1 秒");
        }

        if (lower.Contains("saa_19\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'saa19 tab\there newline\nhere'"}""" }
            }], "SAA19: 工具返回 tab+newline");
        }

        if (lower.Contains("saa_20\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo saa20 && echo saa20_again"}""" }
            }], "SAA20: 工具返回重复内容");
        }

        return (null, "");
    }
}
