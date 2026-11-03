namespace MockAiResponder;

/// <summary>SDD1-SDD20: 流式中断/恢复场景 — 测试流式响应的各种中断和异常情况</summary>
internal static partial class IntentMatcher {
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSDDIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("sdd_1\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd1_normal"}""" }
            }], "SDD1: 流式正常完成");
        }

        if (lower.Contains("sdd_2\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd2_partial"}""" }
            }], "SDD2: 流式部分后中断");
        }

        if (lower.Contains("sdd_3\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd3_no_done"}""" }
            }], "SDD3: 流式无 [DONE] 标记");
        }

        if (lower.Contains("sdd_4\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd4_empty_chunk"}""" }
            }], "SDD4: 流式空 chunk");
        }

        if (lower.Contains("sdd_5\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd5_only_role"}""" }
            }], "SDD5: 流式只有 role chunk");
        }

        if (lower.Contains("sdd_6\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd6_content_then_empty"}""" }
            }], "SDD6: 流式 content 后空 chunk");
        }

        if (lower.Contains("sdd_7\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd7_multi_done"}""" }
            }], "SDD7: 流式多个 [DONE]");
        }

        if (lower.Contains("sdd_8\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd8_done_in_content"}""" }
            }], "SDD8: [DONE] 在 content 中");
        }

        if (lower.Contains("sdd_9\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd9_large_chunk"}""" }
            }], "SDD9: 流式大 chunk");
        }

        if (lower.Contains("sdd_10\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'sdd10_%s ' $(seq 1 50)"}""" }
            }], "SDD10: 流式 50 个 chunk");
        }

        if (lower.Contains("sdd_11\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd11_usage_only"}""" }
            }], "SDD11: 流式只有 usage");
        }

        if (lower.Contains("sdd_12\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd12_no_usage"}""" }
            }], "SDD12: 流式无 usage");
        }

        if (lower.Contains("sdd_13\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd13_finish_length"}""" }
            }], "SDD13: finish_reason=length");
        }

        if (lower.Contains("sdd_14\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd14_finish_filter"}""" }
            }], "SDD14: finish_reason=content_filter");
        }

        if (lower.Contains("sdd_15\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd15_model_change"}""" }
            }], "SDD15: 流式 model 变化");
        }

        if (lower.Contains("sdd_16\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd16_extra_field"}""" }
            }], "SDD16: 流式额外字段");
        }

        if (lower.Contains("sdd_17\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd17_reasoning"}""" }
            }], "SDD17: 流式 reasoning content");
        }

        if (lower.Contains("sdd_18\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd18_multi_choice"}""" }
            }], "SDD18: 流式多 choice");
        }

        if (lower.Contains("sdd_19\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd19_tool_call_stream"}""" }
            }], "SDD19: 流式工具调用");
        }

        if (lower.Contains("sdd_20\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sdd20_interleaved"}""" }
            }], "SDD20: 流式 content+tool 交替");
        }

        return (null, "");
    }
}
