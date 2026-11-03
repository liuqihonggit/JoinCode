namespace MockAiResponder;

/// <summary>SEE1-SEE20: 上下文压缩/token 上限场景 — 测试长上下文和 token 限制</summary>
internal static partial class IntentMatcher {
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSEEIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("see_1\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo see1_short"}""" }
            }], "SEE1: 短上下文");
        }

        if (lower.Contains("see_2\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'see2 %.0s' {1..100}"}""" }
            }], "SEE2: 中等上下文 100 字");
        }

        if (lower.Contains("see_3\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'see3 %.0s' {1..1000}"}""" }
            }], "SEE3: 长上下文 1000 字");
        }

        if (lower.Contains("see_4\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'see4 %.0s' {1..5000}"}""" }
            }], "SEE4: 超长上下文 5000 字");
        }

        if (lower.Contains("see_5\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "seq 1 100"}""" }
            }], "SEE5: 数字序列上下文");
        }

        if (lower.Contains("see_6\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "yes see6_line | head -50"}""" }
            }], "SEE6: 重复行上下文");
        }

        if (lower.Contains("see_7\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo see7 | fold -w 10"}""" }
            }], "SEE7: 折叠上下文");
        }

        if (lower.Contains("see_8\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo see8_a; echo see8_b; echo see8_c"}""" }
            }], "SEE8: 多段上下文");
        }

        if (lower.Contains("see_9\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see9 {\"key\":\"value\"}'"}""" }
            }], "SEE9: JSON 上下文");
        }

        if (lower.Contains("see_10\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see10 <html><body>test</body></html>'"}""" }
            }], "SEE10: HTML 上下文");
        }

        if (lower.Contains("see_11\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see11 # markdown\n- item1\n- item2'"}""" }
            }], "SEE11: Markdown 上下文");
        }

        if (lower.Contains("see_12\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see12 ```python\nprint(1)\n```'"}""" }
            }], "SEE12: 代码块上下文");
        }

        if (lower.Contains("see_13\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see13 中文上下文测试'"}""" }
            }], "SEE13: 中文上下文");
        }

        if (lower.Contains("see_14\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see14 emoji: 😀🎉🚀'"}""" }
            }], "SEE14: emoji 上下文");
        }

        if (lower.Contains("see_15\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'see15\\tcol1\\tcol2\\n'"}""" }
            }], "SEE15: TSV 上下文");
        }

        if (lower.Contains("see_16\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see16,comma,separated,values'"}""" }
            }], "SEE16: CSV 上下文");
        }

        if (lower.Contains("see_17\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see17 | pipe | separated | values'"}""" }
            }], "SEE17: 管道分隔上下文");
        }

        if (lower.Contains("see_18\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see17 key1=val1 key2=val2 key3=val3'"}""" }
            }], "SEE18: 键值对上下文");
        }

        if (lower.Contains("see_19\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'see19 line with    multiple    spaces'"}""" }
            }], "SEE19: 多空格上下文");
        }

        if (lower.Contains("see_20\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'see20\\n\\n\\n\\n\\n'"}""" }
            }], "SEE20: 多空行上下文");
        }

        return (null, "");
    }
}
