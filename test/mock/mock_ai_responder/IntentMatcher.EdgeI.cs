namespace MockAiResponder;

/// <summary>I1-I20: 错误恢复与安全边缘场景 — 从 IntentMatcher 提取的 partial 方法</summary>
internal static partial class IntentMatcher {
    /// <summary>I1-I20: 错误恢复与安全边缘场景匹配</summary>
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeIIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("i1\n") || lower.Contains("fail then retry")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "nonexistent_cmd_i1"}""" }
            }], "I1: 工具失败后 LLM 重试");
        }

        if (lower.Contains("i2\n") || lower.Contains("3 consecutive fails")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "nonexistent_i2", Arguments = "{}" }
            }], "I2: 连续 3 次工具失败");
        }

        if (lower.Contains("i3\n") || lower.Contains("slow command")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "sleep 2 && echo i3_done"}""" }
            }], "I3: 工具执行慢命令");
        }

        if (lower.Contains("i4\n") || lower.Contains("i4 exit code 1")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "exit 1"}""" }
            }], "I4: 工具返回 exit code 1");
        }

        if (lower.Contains("i5\n") || lower.Contains("i5 exit code 127")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "command_not_found_127"}""" }
            }], "I5: 工具返回 exit code 127");
        }

        if (lower.Contains("i6\n") || lower.Contains("i6 exit code 255")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "exit 255"}""" }
            }], "I6: 工具返回 exit code 255");
        }

        if (lower.Contains("i7\n") || lower.Contains("large stderr")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'i7 large stderr output' >&2; exit 1"}""" }
            }], "I7: 工具返回大量 stderr");
        }

        if (lower.Contains("i8\n") || lower.Contains("stdout stderr mix")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo i8_stdout; echo i8_stderr >&2"}""" }
            }], "I8: 工具返回 stdout+stderr 混合");
        }

        if (lower.Contains("i9\n") || lower.Contains("nonexistent tool name")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "nonexistent_tool_i9", Arguments = "{}" }
            }], "I9: 工具名不存在");
        }

        if (lower.Contains("i10\n") || lower.Contains("missing required field")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"other": "value"}""" }
            }], "I10: 工具参数缺失必需字段");
        }

        if (lower.Contains("i11\n") || lower.Contains("wrong param type")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": 12345}""" }
            }], "I11: 工具参数类型错误");
        }

        if (lower.Contains("i12\n") || lower.Contains("empty command")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": ""}""" }
            }], "I12: 工具参数值是空字符串");
        }

        if (lower.Contains("i13\n") || lower.Contains("whitespace command")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "   "}""" }
            }], "I13: 工具参数值是纯空白");
        }

        if (lower.Contains("i14\n") || lower.Contains("injection attempt")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo i14_safe_output"}""" }
            }], "I14: 工具参数值含注入（安全处理）");
        }

        if (lower.Contains("i15\n") || lower.Contains("pipe command")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo i15_a | cat"}""" }
            }], "I15: 工具参数值含管道");
        }

        if (lower.Contains("i16\n") || lower.Contains("redirect command")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo i16_redirect > /tmp/i16_test && cat /tmp/i16_test"}""" }
            }], "I16: 工具参数值含重定向");
        }

        if (lower.Contains("i17\n") || lower.Contains("background command")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo i17_bg & wait"}""" }
            }], "I17: 工具参数值含后台");
        }

        if (lower.Contains("i18\n") || lower.Contains("subshell command")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "(echo i18_subshell)"}""" }
            }], "I18: 工具参数值含子shell");
        }

        if (lower.Contains("i19\n") || lower.Contains("variable command")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo i19_${HOME:+var}"}""" }
            }], "I19: 工具参数值含变量");
        }

        if (lower.Contains("i20\n") || lower.Contains("wildcard command")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo i20_wildcard_*"}""" }
            }], "I20: 工具参数值含通配符");
        }

        return (null, "");
    }
}
