namespace MockAiResponder;

/// <summary>SCC1-SCC20: 并发竞争场景 — 测试多请求/共享状态/竞态条件</summary>
internal static partial class IntentMatcher {
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSCCIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("scc_1\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc1_a"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc1_b"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc1_c"}""" }
                }
            ], "SCC1: 3 并发工具调用");
        }

        if (lower.Contains("scc_2\n")) {
            var calls = new List<ToolCallDto>();
            for (var i = 0; i < 5; i++) {
                calls.Add(new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = "{\"command\": \"echo scc2_" + i + "\"}" }
                });
            }
            return (calls, "SCC2: 5 并发工具调用");
        }

        if (lower.Contains("scc_3\n")) {
            var calls = new List<ToolCallDto>();
            for (var i = 0; i < 10; i++) {
                calls.Add(new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = "{\"command\": \"echo scc3_" + i + "\"}" }
                });
            }
            return (calls, "SCC3: 10 并发工具调用");
        }

        if (lower.Contains("scc_4\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc4_same"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc4_same"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc4_same"}""" }
                }
            ], "SCC4: 并发相同命令");
        }

        if (lower.Contains("scc_5\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc5_unique_1"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc5_unique_2"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc5_unique_3"}""" }
                }
            ], "SCC5: 并发不同命令");
        }

        if (lower.Contains("scc_6\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc6_ok"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc6_error >&2; exit 1"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc6_ok"}""" }
                }
            ], "SCC6: 并发+一个失败");
        }

        if (lower.Contains("scc_7\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "sleep 2 && echo scc7_slow"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc7_fast"}""" }
                }
            ], "SCC7: 并发+一个慢");
        }

        if (lower.Contains("scc_8\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc8_step1 && echo scc8_step2 && echo scc8_step3"}""" }
            }], "SCC8: 串行多步工具");
        }

        if (lower.Contains("scc_9\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc9_serial"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc9_parallel_a"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc9_parallel_b"}""" }
                }
            ], "SCC9: 串行+并发混合");
        }

        if (lower.Contains("scc_10\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc10_data > /tmp/scc10_test && cat /tmp/scc10_test"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc10_override > /tmp/scc10_test && cat /tmp/scc10_test"}""" }
                }
            ], "SCC10: 并发写同一文件");
        }

        if (lower.Contains("scc_11\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc11 > /tmp/scc11_test && cat /tmp/scc11_test"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "cat /tmp/scc11_test"}""" }
                }
            ], "SCC11: 并发读同一文件");
        }

        if (lower.Contains("scc_12\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc12 | cat | cat | cat"}""" }
            }], "SCC12: 管道链");
        }

        if (lower.Contains("scc_13\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "JCC_TEST_SCC13=test echo $JCC_TEST_SCC13"}""" }
            }], "SCC13: 环境变量操作");
        }

        if (lower.Contains("scc_14\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc14 | wc -l"}""" }
            }], "SCC14: 子进程调用");
        }

        if (lower.Contains("scc_15\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "kill -0 $$ && echo scc15_alive"}""" }
            }], "SCC15: 信号检查");
        }

        if (lower.Contains("scc_16\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc16 | head -c 100"}""" }
            }], "SCC16: 内存限制");
        }

        if (lower.Contains("scc_17\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "yes scc17 | head -100"}""" }
            }], "SCC17: CPU 密集");
        }

        if (lower.Contains("scc_18\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "dd if=/dev/zero bs=1024 count=10 2>/dev/null | wc -c"}""" }
            }], "SCC18: 磁盘 I/O");
        }

        if (lower.Contains("scc_19\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc19 | tee /dev/null"}""" }
            }], "SCC19: 网络 I/O 模拟");
        }

        if (lower.Contains("scc_20\n")) {
            return ([
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc20_cpu | wc -c"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc20_mem | head -c 50"}""" }
                },
                new ToolCallDto {
                    Id = $"call_{Guid.NewGuid():N}"[..20],
                    Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo scc20_disk > /tmp/scc20 && cat /tmp/scc20"}""" }
                }
            ], "SCC20: CPU+内存+磁盘混合");
        }

        return (null, "");
    }
}
