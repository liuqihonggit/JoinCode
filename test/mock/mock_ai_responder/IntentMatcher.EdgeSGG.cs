namespace MockAiResponder;

/// <summary>SGG1-SGG20: 多轮工具调用场景 — 测试连续多轮工具调用和工具链路异常</summary>
internal static partial class IntentMatcher {
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSGGIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("sgg_1\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg1_round1"}""" }
            }], "SGG1: 单轮工具调用");
        }

        if (lower.Contains("sgg_2\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg2_step1 && echo sgg2_step2"}""" }
            }], "SGG2: 两步工具调用");
        }

        if (lower.Contains("sgg_3\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg3_step1 && echo sgg3_step2 && echo sgg3_step3"}""" }
            }], "SGG3: 三步工具调用");
        }

        if (lower.Contains("sgg_4\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "seq 1 5 | xargs -I{} echo sgg4_step_{}"}""" }
            }], "SGG4: 五步工具调用");
        }

        if (lower.Contains("sgg_5\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg5_first"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg5_second"}""" }
                }
            ], "SGG5: 两轮并发工具");
        }

        if (lower.Contains("sgg_6\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg6 && exit 1"}""" }
            }], "SGG6: 工具失败 exit 1");
        }

        if (lower.Contains("sgg_7\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "nonexistent_sgg7"}""" }
            }], "SGG7: 工具命令不存在");
        }

        if (lower.Contains("sgg_8\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "sleep 1 && echo sgg8_slow"}""" }
            }], "SGG8: 工具执行慢");
        }

        if (lower.Contains("sgg_9\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg9_stdout; echo sgg9_stderr >&2"}""" }
            }], "SGG9: 工具 stdout+stderr");
        }

        if (lower.Contains("sgg_10\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '{\"sgg10\":\"json\"}'"}""" }
            }], "SGG10: 工具返回 JSON");
        }

        if (lower.Contains("sgg_11\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sgg11 中文输出'"}""" }
            }], "SGG11: 工具中文输出");
        }

        if (lower.Contains("sgg_12\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sgg12 emoji: 😀'"}""" }
            }], "SGG12: 工具 emoji 输出");
        }

        if (lower.Contains("sgg_13\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'sgg13\\tcol1\\tcol2\\n'"}""" }
            }], "SGG13: 工具 TSV 输出");
        }

        if (lower.Contains("sgg_14\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'sgg14,comma,separated'"}""" }
            }], "SGG14: 工具 CSV 输出");
        }

        if (lower.Contains("sgg_15\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '```python\nprint(\"sgg15\")\n```'"}""" }
            }], "SGG15: 工具代码块输出");
        }

        if (lower.Contains("sgg_16\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '# sgg16 markdown\n- item'"}""" }
            }], "SGG16: 工具 markdown 输出");
        }

        if (lower.Contains("sgg_17\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\033[31msgg17 red\\033[0m'"}""" }
            }], "SGG17: 工具 ANSI 输出");
        }

        if (lower.Contains("sgg_18\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg18 | wc -c"}""" }
            }], "SGG18: 工具管道");
        }

        if (lower.Contains("sgg_19\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg19 > /tmp/sgg19 && cat /tmp/sgg19"}""" }
            }], "SGG19: 工具文件读写");
        }

        if (lower.Contains("sgg_20\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg20_a"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg20_b"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo sgg20_c"}""" }
                }
            ], "SGG20: 三轮并发工具");
        }

        return (null, "");
    }
}
