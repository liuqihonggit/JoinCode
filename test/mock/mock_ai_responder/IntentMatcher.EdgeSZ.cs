namespace MockAiResponder;

/// <summary>SZ1-SZ20: 工具执行边缘场景 — 测试工具输出的不舒服点</summary>
internal static partial class IntentMatcher {
    /// <summary>SZ1-SZ20: 工具执行边缘场景匹配</summary>
    private static (List<ToolCallDto>? ToolCalls, string Description) MatchEdgeSZIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();

        if (lower.Contains("sz_1\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "seq 1 10000"}""" }
            }], "SZ1: 超长工具输出 10000 行");
        }

        if (lower.Contains("sz_2\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo $HOME $USER $PATH"}""" }
            }], "SZ2: 工具输出包含环境变量路径");
        }

        if (lower.Contains("sz_3\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\033[31mred\\033[0m green\\n'"}""" }
            }], "SZ3: 工具输出包含 ANSI 转义序列");
        }

        if (lower.Contains("sz_4\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '{\"key\":\"value\",\"nested\":{\"a\":1}}'"}""" }
            }], "SZ4: 工具输出包含嵌套 JSON");
        }

        if (lower.Contains("sz_5\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '```python\\nimport os\\nprint(os.getcwd())\\n```'"}""" }
            }], "SZ5: 工具输出包含 markdown 代码块");
        }

        if (lower.Contains("sz_6\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf '\\xF0\\x9F\\x98\\x80\\xF0\\x9F\\x98\\x84 emoji test\\n'"}""" }
            }], "SZ6: 工具输出包含 emoji");
        }

        if (lower.Contains("sz_7\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "yes | head -c 5000"}""" }
            }], "SZ7: 工具输出超长单行 5000 字符");
        }

        if (lower.Contains("sz_8\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "for i in $(seq 1 500); do echo line_$i; done"}""" }
            }], "SZ8: 工具输出 500 行");
        }

        if (lower.Contains("sz_9\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "printf 'tab\\tnewline\\nreturn\\rbackslash\\\\\\n'"}""" }
            }], "SZ9: 工具输出包含特殊字符");
        }

        if (lower.Contains("sz_10\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo ''"}""" }
            }], "SZ10: 工具输出空字符串");
        }

        if (lower.Contains("sz_11\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '   '"}""" }
            }], "SZ11: 工具输出纯空白");
        }

        if (lower.Contains("sz_12\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'line1\\nline2\\nline3\\nline4\\nline5'"}""" }
            }], "SZ12: 工具输出多行");
        }

        if (lower.Contains("sz_13\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo '中文测试 日本語テスト 한국어 테스트'"}""" }
            }], "SZ13: 工具输出多语言");
        }

        if (lower.Contains("sz_14\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "ls -la /nonexistent_dir_sz14 2>&1 || true"}""" }
            }], "SZ14: 工具输出错误信息但 exit 0");
        }

        if (lower.Contains("sz_15\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'single' && echo 'double' && echo 'triple'"}""" }
            }], "SZ15: 多命令链式执行");
        }

        if (lower.Contains("sz_16\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo hello | tr a-z A-Z"}""" }
            }], "SZ16: 管道命令");
        }

        if (lower.Contains("sz_17\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'before' > /tmp/sz17_test.txt && cat /tmp/sz17_test.txt && rm /tmp/sz17_test.txt"}""" }
            }], "SZ17: 文件重定向命令");
        }

        if (lower.Contains("sz_18\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "MY_VAR=sz18_value echo $MY_VAR"}""" }
            }], "SZ18: 环境变量赋值命令");
        }

        if (lower.Contains("sz_19\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "(echo subshell1; echo subshell2)"}""" }
            }], "SZ19: 子shell 命令");
        }

        if (lower.Contains("sz_20\n")) {
            return ([new ToolCallDto {
                Id = $"call_{Guid.NewGuid():N}"[..20],
                Function = new ToolCallFunctionDto { Name = "bash", Arguments = """{"command": "echo 'before sleep'; sleep 1; echo 'after sleep'"}""" }
            }], "SZ20: 命令中间 sleep");
        }

        return (null, "");
    }
}
