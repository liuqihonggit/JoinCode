namespace MockAiResponder;

/// <summary>SFF1-SFF20: JSON 特殊字符/Unicode/emoji 场景 — 测试各种字符编码边界</summary>
internal static partial class IntentMatcher {
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSFFIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("sff_1\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff1 引号\"测试'"}""" }
            }], "SFF1: JSON 引号字符");
        }

        if (lower.Contains("sff_2\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff2 反斜杠\\测试'"}""" }
            }], "SFF2: JSON 反斜杠字符");
        }

        if (lower.Contains("sff_3\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff3 换行\n测试'"}""" }
            }], "SFF3: JSON 换行字符");
        }

        if (lower.Contains("sff_4\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff4 制表符\t测试'"}""" }
            }], "SFF4: JSON 制表符");
        }

        if (lower.Contains("sff_5\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff5 回车\r测试'"}""" }
            }], "SFF5: JSON 回车字符");
        }

        if (lower.Contains("sff_6\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'sff6 \\u0000 null'"}""" }
            }], "SFF6: null 字节");
        }

        if (lower.Contains("sff_7\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff7 😀 emoji 测试'"}""" }
            }], "SFF7: emoji 字符");
        }

        if (lower.Contains("sff_8\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff8 中文 日本語 한국어'"}""" }
            }], "SFF8: 多语言 CJK");
        }

        if (lower.Contains("sff_9\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff9 αβγδ εζηθ Greek'"}""" }
            }], "SFF9: 希腊字母");
        }

        if (lower.Contains("sff_10\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff10 ∑∏∫√ math symbols'"}""" }
            }], "SFF10: 数学符号");
        }

        if (lower.Contains("sff_11\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff11 →←↑↓ arrows'"}""" }
            }], "SFF11: 箭头符号");
        }

        if (lower.Contains("sff_12\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff12 ©®™ legal symbols'"}""" }
            }], "SFF12: 法律符号");
        }

        if (lower.Contains("sff_13\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'sff13 \\xef\\xbb\\xbf BOM'"}""" }
            }], "SFF13: BOM 字符");
        }

        if (lower.Contains("sff_14\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'sff14 \\x01\\x02\\x03 control'"}""" }
            }], "SFF14: 控制字符");
        }

        if (lower.Contains("sff_15\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff15 —– en dash em dash'"}""" }
            }], "SFF15: dash 变体");
        }

        if (lower.Contains("sff_16\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff16 “”‘’ smart quotes'"}""" }
            }], "SFF16: 智能引号");
        }

        if (lower.Contains("sff_17\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff17 … ellipsis'"}""" }
            }], "SFF17: 省略号");
        }

        if (lower.Contains("sff_18\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff18 ½⅓¼ fractions'"}""" }
            }], "SFF18: 分数符号");
        }

        if (lower.Contains("sff_19\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff19 °℃℉ temperature'"}""" }
            }], "SFF19: 温度符号");
        }

        if (lower.Contains("sff_20\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sff20 ♠♥♦♣ suits'"}""" }
            }], "SFF20: 扑克花色");
        }

        return (null, "");
    }
}
