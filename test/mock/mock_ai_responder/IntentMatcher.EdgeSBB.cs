namespace MockAiResponder;

/// <summary>SBB1-SBB20: 工具参数边界值场景 — 测试工具调用参数的各种边界值</summary>
internal static partial class IntentMatcher {
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSBBIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("sbb_1\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": ""}""" }
            }], "SBB1: 空字符串命令参数");
        }

        if (lower.Contains("sbb_2\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": null}""" }
            }], "SBB2: null 命令参数");
        }

        if (lower.Contains("sbb_3\n")) {
            var longCmd = new string('x', 10000);
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = "{\"command\": \"echo " + longCmd + "\"}" }
            }], "SBB3: 超长命令参数 10KB");
        }

        if (lower.Contains("sbb_4\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '{\"nested\":{\"deep\":{\"value\":42}}}'"}""" }
            }], "SBB4: 嵌套 JSON 参数");
        }

        if (lower.Contains("sbb_5\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '中文 日本語 한국어'}"}""" }
            }], "SBB5: Unicode 参数");
        }

        if (lower.Contains("sbb_6\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\x01\\x02\\x03 sbb6'}"}""" }
            }], "SBB6: 控制字符参数");
        }

        if (lower.Contains("sbb_7\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'SELECT * FROM users; DROP TABLE users; --'}"}""" }
            }], "SBB7: SQL 注入参数");
        }

        if (lower.Contains("sbb_8\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '../../../etc/passwd'}"}""" }
            }], "SBB8: 路径遍历参数");
        }

        if (lower.Contains("sbb_9\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo test; echo malicious"}""" }
            }], "SBB9: 命令注入参数");
        }

        if (lower.Contains("sbb_10\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sbb10_a"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sbb10_b"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sbb10_c"}""" }
                }
            ], "SBB10: 3 个并发工具调用");
        }

        if (lower.Contains("sbb_11\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'quote\" and backslash\\ and tab\there'"}""" }
            }], "SBB11: 混合特殊字符参数");
        }

        if (lower.Contains("sbb_12\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '[1, 2, 3, \"array\", true, null]'"}""" }
            }], "SBB12: 数组参数值");
        }

        if (lower.Contains("sbb_13\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo true"}""" }
            }], "SBB13: 布尔参数值");
        }

        if (lower.Contains("sbb_14\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 42"}""" }
            }], "SBB14: 数字参数值");
        }

        if (lower.Contains("sbb_15\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo null"}""" }
            }], "SBB15: null 参数值");
        }

        if (lower.Contains("sbb_16\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo undefined"}""" }
            }], "SBB16: undefined 参数值");
        }

        if (lower.Contains("sbb_17\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '{\"l1\":{\"l2\":{\"l3\":{\"l4\":{\"l5\":\"deep\"}}}}}'"}""" }
            }], "SBB17: 5 层深嵌套参数");
        }

        if (lower.Contains("sbb_18\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\xef\\xbb\\xbf sbb18 bom'}"}""" }
            }], "SBB18: BOM 参数");
        }

        if (lower.Contains("sbb_19\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'emoji: 😀🎉🚀🔥💯'}"}""" }
            }], "SBB19: emoji 参数");
        }

        if (lower.Contains("sbb_20\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'line1\\nline2\\nline3\\nline4\\nline5'"}""" }
            }], "SBB20: 多行换行参数");
        }

        return (null, "");
    }
}
