namespace MockAiResponder;

/// <summary>SX1-SX20: 流式协议边缘场景 — 测试 jcc 对异常 chunk 模式的处理</summary>
internal static partial class IntentMatcher {
    /// <summary>SX1-SX20: 流式协议边缘场景匹配</summary>
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSxIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("sx_1\n") || lower.Contains("usage in first chunk")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx1_usage_first"}""" }
            }], "SX1: usage 在第一个 chunk（非最后一个）");
        }

        if (lower.Contains("sx_2\n") || lower.Contains("multiple choices")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx2_multi_choice"}""" }
            }], "SX2: 多 choices 响应");
        }

        if (lower.Contains("sx_3\n") || lower.Contains("no usage at all")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx3_no_usage"}""" }
            }], "SX3: 完全无 usage 字段");
        }

        if (lower.Contains("sx_4\n") || lower.Contains("finish then content")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx4_finish_then_content"}""" }
            }], "SX4: finish_reason 后仍有 content chunk");
        }

        if (lower.Contains("sx_5\n") || lower.Contains("content then finish then content")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx5_content_finish_content"}""" }
            }], "SX5: content→finish→content 交错");
        }

        if (lower.Contains("sx_6\n") || lower.Contains("empty delta chunks")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx6_empty_delta"}""" }
            }], "SX6: 多个空 delta chunk 后才有内容");
        }

        if (lower.Contains("sx_7\n") || lower.Contains("role only no content")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx7_role_only"}""" }
            }], "SX7: 只有 role 无 content 的 chunk");
        }

        if (lower.Contains("sx_8\n") || lower.Contains("tool call split mid argument")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx8_split_mid_arg"}""" }
            }], "SX8: tool_call 参数在 JSON 中间被分割");
        }

        if (lower.Contains("sx_9\n") || lower.Contains("tool call index gap")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx9_index_gap"}""" }
            }], "SX9: tool_call index 跳跃（0→2，跳过1）");
        }

        if (lower.Contains("sx_10\n") || lower.Contains("reasoning then content then tool")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx10_reasoning_content_tool"}""" }
            }], "SX10: reasoning→content→tool_call 三段式");
        }

        if (lower.Contains("sx_11\n") || lower.Contains("very long reasoning")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx11_long_reasoning"}""" }
            }], "SX11: 超长 reasoning_content（5000字）");
        }

        if (lower.Contains("sx_12\n") || lower.Contains("unicode in reasoning")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx12_unicode_reasoning"}""" }
            }], "SX12: reasoning_content 含 Unicode/emoji");
        }

        if (lower.Contains("sx_13\n") || lower.Contains("model field changes")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx13_model_changes"}""" }
            }], "SX13: chunk 中 model 字段中途变化");
        }

        if (lower.Contains("sx_14\n") || lower.Contains("system fingerprint")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx14_fingerprint"}""" }
            }], "SX14: system_fingerprint 字段存在");
        }

        if (lower.Contains("sx_15\n") || lower.Contains("logprobs field")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx15_logprobs"}""" }
            }], "SX15: logprobs 字段存在");
        }

        if (lower.Contains("sx_16\n") || lower.Contains("two done markers")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx16_two_done"}""" }
            }], "SX16: 两个 [DONE] 标记");
        }

        if (lower.Contains("sx_17\n") || lower.Contains("done with data")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx17_done_with_data"}""" }
            }], "SX17: [DONE] 前有额外 data chunk");
        }

        if (lower.Contains("sx_18\n") || lower.Contains("chunk with extra fields")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx18_extra_fields"}""" }
            }], "SX18: chunk 含未知额外字段");
        }

        if (lower.Contains("sx_19\n") || lower.Contains("negative token count")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx19_negative_tokens"}""" }
            }], "SX19: usage 含负数 token 计数");
        }

        if (lower.Contains("sx_20\n") || lower.Contains("zero token count")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sx20_zero_tokens"}""" }
            }], "SX20: usage 全零 token 计数");
        }

        return (null, "");
    }
}
