namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// VB 组内置工具 E2E 脚本 — 覆盖 vb01-vb35 内置工具行为助记符。
/// 每个助记符对应一个 ConversationScript，验证内置工具的特定行为信号。
/// vb03/vb05/vb09/vb14 因 ConversationScript 无法表达真实超时/大输出/大文件/未读先写状态，
/// 标注 TODO 暂用模拟方式覆盖工具调用链路，真实场景需手动 exe 验证（ADR 0080）。
/// </summary>
public static class VbBuiltinToolScripts {
    // ============================================================
    // vb01-vb05: bash 工具
    // ============================================================

    /// <summary>
    /// vb01: bash 成功执行 — pipeline 正常完成 exit=0，
    /// 可观测信号 ToolResult success + stdout。
    /// </summary>
    public static ConversationScript Vb01BashSuccess => new() {
        Name = "vb01 bash 成功执行",
        Turns =
        [
            new ConversationTurn {
                UserInput = "执行 echo 命令",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "echo vb01_success" },
                            ToolResult = "vb01_success"
                        }
                    ],
                    FollowUpText = "命令执行成功，输出: vb01_success"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "vb01: 应包含 bash 工具调用" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "bash", Description = "vb01: bash 工具应执行成功" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb01: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb01: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb02: bash 失败 (exit!=0) — 命令返回非零退出码，
    /// 可观测信号 ToolResult success + stderr + exit code。
    /// </summary>
    public static ConversationScript Vb02BashFailure => new() {
        Name = "vb02 bash 失败退出码非零",
        Turns =
        [
            new ConversationTurn {
                UserInput = "执行一个会失败的命令",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "exit 1" },
                            ToolResult = "exit code: 1"
                        }
                    ],
                    FollowUpText = "命令执行失败，退出码: 1"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "vb02: 应包含 bash 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "退出码", Description = "vb02: 应包含退出码诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb02: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb03: bash 超时 — 命令执行超时，可观测信号 超时错误诊断。
    /// TODO: MockServer 模拟环境下不会真实触发 bash 命令超时（ToolResult 是预设值），
    /// 此脚本仅覆盖工具调用链路。真实超时场景需手动 exe 验证（ADR 0080）：
    /// jcc bash --command "sleep 100" --timeout 1s。
    /// </summary>
    public static ConversationScript Vb03BashTimeout => new() {
        Name = "vb03 bash 超时",
        Turns =
        [
            new ConversationTurn {
                UserInput = "执行一个会超时的命令",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "sleep 100", timeout = 1 },
                            ToolResult = "命令执行超时，已终止"
                        }
                    ],
                    FollowUpText = "命令执行超时，已终止"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "vb03: 应包含 bash 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "超时", Description = "vb03: 应包含超时诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb03: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb04: bash 命令不存在 — 执行不存在的命令，
    /// 可观测信号 "command not found" + exit 127。
    /// </summary>
    public static ConversationScript Vb04BashCommandNotFound => new() {
        Name = "vb04 bash 命令不存在",
        Turns =
        [
            new ConversationTurn {
                UserInput = "执行一个不存在的命令",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "nonexistent_command_xyz_vb04" },
                            ToolResult = "command not found, exit 127"
                        }
                    ],
                    FollowUpText = "命令不存在: nonexistent_command_xyz_vb04，exit 127"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "vb04: 应包含 bash 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "command not found", Description = "vb04: 应包含 command not found 诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb04: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb05: bash 超大 stdout 截断 — stdout 超过截断阈值，
    /// 可观测信号 截断后的输出 + hidden lines。
    /// TODO: MockServer 模拟环境下不会真实产生超大输出（ToolResult 是预设值），
    /// 此脚本仅覆盖工具调用链路。真实截断场景需手动 exe 验证（ADR 0080）：
    /// jcc bash --command "yes vb05 | head -100000"。
    /// </summary>
    public static ConversationScript Vb05BashLargeOutputTruncated => new() {
        Name = "vb05 bash 超大 stdout 截断",
        Turns =
        [
            new ConversationTurn {
                UserInput = "执行一个产生大量输出的命令",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "yes vb05" },
                            ToolResult = "vb05\nvb05\n... (truncated, hidden lines: 99998)"
                        }
                    ],
                    FollowUpText = "输出超过截断阈值，已截断，hidden lines: 99998"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "vb05: 应包含 bash 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "截断", Description = "vb05: 应包含截断诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb05: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb06-vb11: read 工具
    // ============================================================

    /// <summary>
    /// vb06: read 成功 — 读取存在的文件，可观测信号 带行号内容。
    /// </summary>
    public static ConversationScript Vb06ReadSuccess => new() {
        Name = "vb06 read 成功",
        Turns =
        [
            new ConversationTurn {
                UserInput = "读取 vb06_test.txt 文件内容",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "read",
                            Arguments = new { file_path = "vb06_test.txt" },
                            ToolResult = "1: hello vb06"
                        }
                    ],
                    FollowUpText = "文件内容已读取，带行号: 1: hello vb06"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "read", Description = "vb06: 应包含 read 工具调用" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "read", Description = "vb06: read 工具应执行成功" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb06: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb06: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb07: read 文件不存在 — 读取不存在的文件，
    /// 可观测信号 FileNotFound 诊断 + 相似文件建议。
    /// </summary>
    public static ConversationScript Vb07ReadFileNotFound => new() {
        Name = "vb07 read 文件不存在",
        Turns =
        [
            new ConversationTurn {
                UserInput = "读取一个不存在的文件",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "read",
                            Arguments = new { file_path = "vb07_nonexistent.txt" },
                            ToolResult = "FileNotFound: vb07_nonexistent.txt"
                        }
                    ],
                    FollowUpText = "文件不存在: FileNotFound，相似文件建议: vb06_test.txt"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "read", Description = "vb07: 应包含 read 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "FileNotFound", Description = "vb07: 应包含 FileNotFound 诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb07: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb08: read 目录而非文件 — 路径是目录，
    /// 可观测信号 "IsDirectoryNotFile" 诊断。
    /// </summary>
    public static ConversationScript Vb08ReadDirectoryNotFile => new() {
        Name = "vb08 read 目录而非文件",
        Turns =
        [
            new ConversationTurn {
                UserInput = "读取一个目录路径",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "read",
                            Arguments = new { file_path = "." },
                            ToolResult = "IsDirectoryNotFile: ."
                        }
                    ],
                    FollowUpText = "路径是目录而非文件: IsDirectoryNotFile"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "read", Description = "vb08: 应包含 read 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "IsDirectoryNotFile", Description = "vb08: 应包含 IsDirectoryNotFile 诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb08: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb09: read 超大文件 token 超限 — 文件 > 25000 tokens，
    /// 可观测信号 "TokenLimitExceeded" 诊断。
    /// TODO: 需要真实大文件（>25000 tokens），MockServer 模拟环境下无法构造。
    /// 此脚本仅覆盖工具调用链路。真实场景需手动 exe 验证（ADR 0080）：
    /// 创建 >100KB 文件后 jcc read --file_path large.txt。
    /// </summary>
    public static ConversationScript Vb09ReadTokenLimitExceeded => new() {
        Name = "vb09 read 超大文件 token 超限",
        Turns =
        [
            new ConversationTurn {
                UserInput = "读取一个超大文件",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "read",
                            Arguments = new { file_path = "vb09_large.txt" },
                            ToolResult = "TokenLimitExceeded: 25000 tokens"
                        }
                    ],
                    FollowUpText = "文件超过 25000 tokens 限制: TokenLimitExceeded"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "read", Description = "vb09: 应包含 read 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "TokenLimitExceeded", Description = "vb09: 应包含 TokenLimitExceeded 诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb09: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb10: read 二进制文件拒绝 — .exe/.dll 等扩展名，
    /// 可观测信号 "This tool cannot read binary files"。
    /// </summary>
    public static ConversationScript Vb10ReadBinaryRejected => new() {
        Name = "vb10 read 二进制文件拒绝",
        Turns =
        [
            new ConversationTurn {
                UserInput = "读取一个二进制文件",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "read",
                            Arguments = new { file_path = "vb10_test.exe" },
                            ToolResult = "This tool cannot read binary files"
                        }
                    ],
                    FollowUpText = "无法读取二进制文件: This tool cannot read binary files"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "read", Description = "vb10: 应包含 read 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "binary files", Description = "vb10: 应包含 binary files 拒绝诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb10: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb11: read 图像文件 (base64) — .png/.jpg 等图像，
    /// 可观测信号 base64 编码内容。
    /// </summary>
    public static ConversationScript Vb11ReadImageBase64 => new() {
        Name = "vb11 read 图像文件 base64",
        Turns =
        [
            new ConversationTurn {
                UserInput = "读取一个 PNG 图像文件",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "read",
                            Arguments = new { file_path = "vb11_test.png" },
                            ToolResult = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACUlEQVR4nGNgYAAAAAMAASsJmQAAAAASUVORK5CYII="
                        }
                    ],
                    FollowUpText = "图像文件已读取，返回 base64 编码内容"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "read", Description = "vb11: 应包含 read 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "base64", Description = "vb11: 应包含 base64 编码内容" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb11: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb12-vb14: write 工具
    // ============================================================

    /// <summary>
    /// vb12: write 创建新文件 — 写入不存在的新路径，
    /// 可观测信号 "File created successfully at: {path}"。
    /// </summary>
    public static ConversationScript Vb12WriteCreateNewFile => new() {
        Name = "vb12 write 创建新文件",
        Turns =
        [
            new ConversationTurn {
                UserInput = "创建一个新文件 vb12_new.txt",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "write",
                            Arguments = new { file_path = "vb12_new.txt", content = "hello vb12" },
                            ToolResult = "File created successfully at: vb12_new.txt"
                        }
                    ],
                    FollowUpText = "文件创建成功: File created successfully at: vb12_new.txt"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "write", Description = "vb12: 应包含 write 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "File created successfully", Description = "vb12: 应包含 File created successfully 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb12: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb12: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb13: write 覆盖已有文件 — 写入已存在的文件，
    /// 可观测信号 "has been updated successfully" + StructuredPatch。
    /// </summary>
    public static ConversationScript Vb13WriteOverwriteExisting => new() {
        Name = "vb13 write 覆盖已有文件",
        Turns =
        [
            new ConversationTurn {
                UserInput = "覆盖写入已存在的文件 vb13_existing.txt",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "write",
                            Arguments = new { file_path = "vb13_existing.txt", content = "updated vb13" },
                            ToolResult = "has been updated successfully"
                        }
                    ],
                    FollowUpText = "文件已更新: has been updated successfully"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "write", Description = "vb13: 应包含 write 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "updated successfully", Description = "vb13: 应包含 updated successfully 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb13: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb14: write 未读先写拒绝 — 未先 read 就 write，
    /// 可观测信号 "必须先读再写" 拒绝。
    /// TODO: 需要 jcc 内部"先读再写"状态跟踪，MockServer 模拟环境下工具调用总是成功，
    /// 此脚本仅覆盖工具调用链路。真实场景需手动 exe 验证（ADR 0080）：
    /// 启动 jcc 后直接 write 不先 read。
    /// </summary>
    public static ConversationScript Vb14WriteWithoutReadRejected => new() {
        Name = "vb14 write 未读先写拒绝",
        Turns =
        [
            new ConversationTurn {
                UserInput = "直接写入一个未读过的文件",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "write",
                            Arguments = new { file_path = "vb14_unread.txt", content = "vb14" },
                            ToolResult = "必须先读再写"
                        }
                    ],
                    FollowUpText = "拒绝写入: 必须先读再写"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "write", Description = "vb14: 应包含 write 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "必须先读再写", Description = "vb14: 应包含必须先读再写拒绝" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb14: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb15-vb17: edit 工具
    // ============================================================

    /// <summary>
    /// vb15: edit 成功 — 精确匹配 oldString 并替换，
    /// 可观测信号 "updated successfully" + StructuredPatch。
    /// </summary>
    public static ConversationScript Vb15EditSuccess => new() {
        Name = "vb15 edit 成功",
        Turns =
        [
            new ConversationTurn {
                UserInput = "编辑文件替换 old 为 new",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "edit",
                            Arguments = new { file_path = "vb15_test.txt", old_string = "old", new_string = "new" },
                            ToolResult = "updated successfully"
                        }
                    ],
                    FollowUpText = "文件已更新: updated successfully"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "edit", Description = "vb15: 应包含 edit 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "updated successfully", Description = "vb15: 应包含 updated successfully 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb15: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb15: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb16: edit oldString 未找到 — oldString 不在文件中，
    /// 可观测信号 "oldString not found" 诊断。
    /// </summary>
    public static ConversationScript Vb16EditOldStringNotFound => new() {
        Name = "vb16 edit oldString 未找到",
        Turns =
        [
            new ConversationTurn {
                UserInput = "编辑文件但 oldString 不存在",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "edit",
                            Arguments = new { file_path = "vb16_test.txt", old_string = "nonexistent_vb16", new_string = "new" },
                            ToolResult = "oldString not found in content"
                        }
                    ],
                    FollowUpText = "编辑失败: oldString not found"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "edit", Description = "vb16: 应包含 edit 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "oldString not found", Description = "vb16: 应包含 oldString not found 诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb16: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb17: edit 多处出现 + replaceAll — oldString 多处出现，
    /// replaceAll=false → "multiple occurrences"; replaceAll=true → "All N replaced"。
    /// 此脚本测试 replaceAll=true 场景。
    /// </summary>
    public static ConversationScript Vb17EditReplaceAll => new() {
        Name = "vb17 edit replaceAll 多处替换",
        Turns =
        [
            new ConversationTurn {
                UserInput = "编辑文件替换所有匹配项",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "edit",
                            Arguments = new { file_path = "vb17_test.txt", old_string = "dup", new_string = "uniq", replace_all = true },
                            ToolResult = "All 3 replaced"
                        }
                    ],
                    FollowUpText = "已替换所有匹配: All 3 replaced"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "edit", Description = "vb17: 应包含 edit 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "replaced", Description = "vb17: 应包含 replaced 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb17: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb18-vb19: glob 工具
    // ============================================================

    /// <summary>
    /// vb18: glob 匹配 — pattern 匹配文件，可观测信号 匹配文件路径列表。
    /// </summary>
    public static ConversationScript Vb18GlobMatch => new() {
        Name = "vb18 glob 匹配",
        Turns =
        [
            new ConversationTurn {
                UserInput = "查找所有 txt 文件",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "glob",
                            Arguments = new { pattern = "**/*.txt" },
                            ToolResult = "vb06_test.txt\nvb12_new.txt"
                        }
                    ],
                    FollowUpText = "找到 2 个匹配文件"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "glob", Description = "vb18: 应包含 glob 工具调用" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "glob", Description = "vb18: glob 工具应执行成功" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb18: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb18: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb19: glob 无匹配 — pattern 无匹配，可观测信号 "NoResults" 诊断。
    /// </summary>
    public static ConversationScript Vb19GlobNoMatch => new() {
        Name = "vb19 glob 无匹配",
        Turns =
        [
            new ConversationTurn {
                UserInput = "查找一个不存在的文件扩展名",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "glob",
                            Arguments = new { pattern = "**/*.nonexistent_ext_vb19" },
                            ToolResult = "NoResults"
                        }
                    ],
                    FollowUpText = "无匹配文件: NoResults"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "glob", Description = "vb19: 应包含 glob 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "NoResults", Description = "vb19: 应包含 NoResults 诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb19: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb20-vb21: grep 工具
    // ============================================================

    /// <summary>
    /// vb20: grep 匹配 — 正则匹配内容，可观测信号 匹配行内容 + 分页。
    /// </summary>
    public static ConversationScript Vb20GrepMatch => new() {
        Name = "vb20 grep 匹配",
        Turns =
        [
            new ConversationTurn {
                UserInput = "搜索代码中的 vb20 标记",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "grep",
                            Arguments = new { pattern = "vb20", include = "*.cs" },
                            ToolResult = "vb20_test.cs:1: vb20 marker"
                        }
                    ],
                    FollowUpText = "在 1 个文件中找到 1 个匹配"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "grep", Description = "vb20: 应包含 grep 工具调用" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "grep", Description = "vb20: grep 工具应执行成功" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb20: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb20: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb21: grep 无匹配 — 正则无匹配，可观测信号 "NoResults" 诊断。
    /// </summary>
    public static ConversationScript Vb21GrepNoMatch => new() {
        Name = "vb21 grep 无匹配",
        Turns =
        [
            new ConversationTurn {
                UserInput = "搜索一个不存在的模式",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "grep",
                            Arguments = new { pattern = "vb21_nonexistent_pattern_xyz", include = "*.cs" },
                            ToolResult = "NoResults"
                        }
                    ],
                    FollowUpText = "无匹配内容: NoResults"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "grep", Description = "vb21: 应包含 grep 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "NoResults", Description = "vb21: 应包含 NoResults 诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb21: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb22-vb24: todo 工具
    // ============================================================

    /// <summary>
    /// vb22: todo_write 创建 — 写入待办列表，可观测信号 "successfully written"。
    /// </summary>
    public static ConversationScript Vb22TodoWriteCreate => new() {
        Name = "vb22 todo_write 创建",
        Turns =
        [
            new ConversationTurn {
                UserInput = "创建一个待办列表",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "todo_write",
                            Arguments = new {
                                todos = new[] {
                                    new { id = "1", content = "vb22 task 1", status = "pending" },
                                    new { id = "2", content = "vb22 task 2", status = "pending" }
                                }
                            },
                            ToolResult = "successfully written"
                        }
                    ],
                    FollowUpText = "待办列表已写入: successfully written"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "todo_write", Description = "vb22: 应包含 todo_write 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "successfully written", Description = "vb22: 应包含 successfully written 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb22: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb22: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb23: todo_list 列表 — 列出待办，可观测信号 Statistics + Todo List。
    /// </summary>
    public static ConversationScript Vb23TodoListList => new() {
        Name = "vb23 todo_list 列表",
        Turns =
        [
            new ConversationTurn {
                UserInput = "列出当前待办",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "todo_list",
                            Arguments = new { },
                            ToolResult = "Statistics: 2 total, 0 completed\nTodo List: [1] vb22 task 1 (pending), [2] vb22 task 2 (pending)"
                        }
                    ],
                    FollowUpText = "待办列表已列出: 2 total, 0 completed"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "todo_list", Description = "vb23: 应包含 todo_list 工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb23: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb23: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb24: todo_update 状态更新 — 更新待办状态，可观测信号 "updated successfully"。
    /// </summary>
    public static ConversationScript Vb24TodoUpdateStatus => new() {
        Name = "vb24 todo_update 状态更新",
        Turns =
        [
            new ConversationTurn {
                UserInput = "更新待办状态为已完成",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "todo_update",
                            Arguments = new { todo_id = "1", status = "completed" },
                            ToolResult = "updated successfully"
                        }
                    ],
                    FollowUpText = "待办状态已更新: updated successfully"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "todo_update", Description = "vb24: 应包含 todo_update 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "updated successfully", Description = "vb24: 应包含 updated successfully 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb24: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb25-vb26: task 工具
    // ============================================================

    /// <summary>
    /// vb25: task_create 创建任务 — 创建任务，可观测信号 "TaskCreated"。
    /// </summary>
    public static ConversationScript Vb25TaskCreate => new() {
        Name = "vb25 task_create 创建任务",
        Turns =
        [
            new ConversationTurn {
                UserInput = "创建一个新任务",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "task_create",
                            Arguments = new { title = "vb25 task", description = "vb25 test task" },
                            ToolResult = "TaskCreated: vb25-task-001"
                        }
                    ],
                    FollowUpText = "任务已创建: TaskCreated"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "task_create", Description = "vb25: 应包含 task_create 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "TaskCreated", Description = "vb25: 应包含 TaskCreated 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb25: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb25: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb26: task_list 列表 — 列出任务，可观测信号 TaskList + 任务摘要。
    /// </summary>
    public static ConversationScript Vb26TaskListList => new() {
        Name = "vb26 task_list 列表",
        Turns =
        [
            new ConversationTurn {
                UserInput = "列出当前所有任务",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "task_list",
                            Arguments = new { },
                            ToolResult = "TaskList: [vb25-task-001] vb25 task (active)"
                        }
                    ],
                    FollowUpText = "任务列表已获取: 1 个任务"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "task_list", Description = "vb26: 应包含 task_list 工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb26: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb26: 不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // vb27-vb28: skill 工具
    // ============================================================

    /// <summary>
    /// vb27: skill 加载 — 加载存在的 skill，可观测信号 "LabelSkill" + Output。
    /// </summary>
    public static ConversationScript Vb27SkillLoad => new() {
        Name = "vb27 skill 加载",
        Turns =
        [
            new ConversationTurn {
                UserInput = "加载 test-skill",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "skill",
                            Arguments = new { name = "test-skill" },
                            ToolResult = "LabelSkill: test-skill loaded successfully"
                        }
                    ],
                    FollowUpText = "Skill 已加载: LabelSkill"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "skill", Description = "vb27: 应包含 skill 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "LabelSkill", Description = "vb27: 应包含 LabelSkill 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb27: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb27: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb28: skill 不存在 — 加载不存在的 skill，可观测信号 "SkillNotFound"。
    /// </summary>
    public static ConversationScript Vb28SkillNotFound => new() {
        Name = "vb28 skill 不存在",
        Turns =
        [
            new ConversationTurn {
                UserInput = "加载一个不存在的 skill",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "skill",
                            Arguments = new { name = "nonexistent_skill_vb28" },
                            ToolResult = "SkillNotFound: nonexistent_skill_vb28"
                        }
                    ],
                    FollowUpText = "Skill 不存在: SkillNotFound"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "skill", Description = "vb28: 应包含 skill 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "SkillNotFound", Description = "vb28: 应包含 SkillNotFound 诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb28: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb29-vb31: webfetch 工具
    // ============================================================

    /// <summary>
    /// vb29: webfetch 成功 — 抓取成功 URL，
    /// 可观测信号 processedResult + http_status_code。
    /// </summary>
    public static ConversationScript Vb29WebfetchSuccess => new() {
        Name = "vb29 webfetch 成功",
        Turns =
        [
            new ConversationTurn {
                UserInput = "抓取 https://example.com/vb29",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "webfetch",
                            Arguments = new { url = "https://example.com/vb29" },
                            ToolResult = "processedResult: Example Domain, http_status_code: 200"
                        }
                    ],
                    FollowUpText = "网络资源获取成功，http_status_code: 200"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "webfetch", Description = "vb29: 应包含 webfetch 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "200", Description = "vb29: 应包含 http_status_code 200" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb29: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb29: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb30: webfetch 重定向 — URL 返回 301/307，
    /// 可观测信号 "REDIRECT DETECTED" + 重定向 URL。
    /// </summary>
    public static ConversationScript Vb30WebfetchRedirect => new() {
        Name = "vb30 webfetch 重定向",
        Turns =
        [
            new ConversationTurn {
                UserInput = "抓取一个会重定向的 URL",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "webfetch",
                            Arguments = new { url = "https://example.com/vb30-redirect" },
                            ToolResult = "REDIRECT DETECTED: https://example.com/vb30-target"
                        }
                    ],
                    FollowUpText = "检测到重定向: REDIRECT DETECTED，目标 URL: https://example.com/vb30-target"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "webfetch", Description = "vb30: 应包含 webfetch 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "REDIRECT", Description = "vb30: 应包含 REDIRECT 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb30: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb31: webfetch 失败 (非200) — URL 返回 500，
    /// 可观测信号 "FetchFailed" 诊断 + statusCode。
    /// </summary>
    public static ConversationScript Vb31WebfetchFailure => new() {
        Name = "vb31 webfetch 失败非200",
        Turns =
        [
            new ConversationTurn {
                UserInput = "抓取一个返回 500 的 URL",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "webfetch",
                            Arguments = new { url = "https://example.com/vb31-error" },
                            ToolResult = "FetchFailed: statusCode 500"
                        }
                    ],
                    FollowUpText = "获取失败: FetchFailed statusCode: 500"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "webfetch", Description = "vb31: 应包含 webfetch 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "FetchFailed", Description = "vb31: 应包含 FetchFailed 诊断" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb31: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb32-vb33: question 工具
    // ============================================================

    /// <summary>
    /// vb32: question 提问+回答 — ask_user_question 工具调用，
    /// 可观测信号 "User has answered your questions"。
    /// </summary>
    public static ConversationScript Vb32QuestionAskAndAnswer => new() {
        Name = "vb32 question 提问回答",
        Turns =
        [
            new ConversationTurn {
                UserInput = "问我是否确认操作",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "question",
                            Arguments = new {
                                question = "vb32 确认执行操作吗？",
                                options = new[] {
                                    new { label = "是", description = "确认执行" },
                                    new { label = "否", description = "取消操作" }
                                }
                            },
                            ToolResult = "User has answered your questions: 是"
                        }
                    ],
                    FollowUpText = "用户已回答: User has answered your questions"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "question", Description = "vb32: 应包含 question 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "answered", Description = "vb32: 应包含 answered 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb32: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb32: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vb33: question 选项校验 — 选项 <2 或 >4，
    /// 可观测信号 "must have at least 2 options"。
    /// </summary>
    public static ConversationScript Vb33QuestionOptionValidation => new() {
        Name = "vb33 question 选项校验",
        Turns =
        [
            new ConversationTurn {
                UserInput = "问一个问题但只给一个选项",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "question",
                            Arguments = new {
                                question = "vb33 只有一个选项？",
                                options = new[] {
                                    new { label = "唯一选项", description = "仅一个选项" }
                                }
                            },
                            ToolResult = "must have at least 2 options"
                        }
                    ],
                    FollowUpText = "选项校验失败: must have at least 2 options"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "question", Description = "vb33: 应包含 question 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "at least 2 options", Description = "vb33: 应包含 at least 2 options 校验错误" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb33: 应有后续 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vb34-vb35: agent 工具
    // ============================================================

    /// <summary>
    /// vb34: agent 创建子代理 — agent 工具调用，
    /// 可观测信号 "Agent started" + Agent ID。
    /// </summary>
    public static ConversationScript Vb34AgentCreateSubAgent => new() {
        Name = "vb34 agent 创建子代理",
        Turns =
        [
            new ConversationTurn {
                UserInput = "帮我执行一个子代理任务",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent",
                            Arguments = new { name = "vb34-agent", description = "vb34 子代理", prompt = "执行 vb34 测试任务" },
                            ToolResult = "Agent started: vb34-agent-001"
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent", Description = "vb34: 应包含 agent 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Agent started", Description = "vb34: 应包含 Agent started 信号" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb34: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb34: 不应有错误" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "vb34 子代理执行完成，任务结果: success"
        ]
    };

    /// <summary>
    /// vb35: agent_status 查询 — agent_status 工具调用，
    /// 可观测信号 status + ID + Description。
    /// </summary>
    public static ConversationScript Vb35AgentStatusQuery => new() {
        Name = "vb35 agent_status 查询",
        Turns =
        [
            new ConversationTurn {
                UserInput = "查询子代理 vb34-agent-001 的状态",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "agent_status",
                            Arguments = new { agent_id = "vb34-agent-001" },
                            ToolResult = "status: running, ID: vb34-agent-001, Description: vb34 子代理"
                        }
                    ],
                    FollowUpText = "Agent 状态: running, ID: vb34-agent-001"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent_status", Description = "vb35: 应包含 agent_status 工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vb35: 应有后续 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vb35: 不应有错误" },
                ]
            }
        ]
    };
}
