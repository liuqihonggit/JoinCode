namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// VC 组 MCP 工具+外部工具 E2E 脚本 — 覆盖 vc01-vc25 行为助记符。
/// 每个助记符对应一个 ConversationScript，验证 MCP 工具/外部工具的特定行为信号。
/// vc01-vc03/vc07-vc09/vc18-vc19 为 CLI 子命令，NonInteractive 模式覆盖调用链路，
/// 真实子命令行为需手动 exe 验证（ADR 0080）。
/// vc21/vc23-vc25 需特殊环境（权限模式/ToolInterventionManager），标注 TODO。
/// </summary>
public static class VcMcpToolScripts {
    // ============================================================
    // vc01-vc03: mcp_serve HTTP 服务端
    // ============================================================

    /// <summary>
    /// vc01: mcp_serve HTTP 启动 — jcc mcp_serve --transport http --port N，
    /// 可观测信号 HTTP 服务监听 + token 输出。
    /// TODO: mcp_serve 是服务端命令，MockServer 模拟环境下无法真正启动 HTTP 服务。
    /// 此脚本用 NonInteractive 模式覆盖调用链路。真实场景需手动 exe 验证（ADR 0080）：
    /// jcc mcp_serve --transport http --port 8080。
    /// </summary>
    public static ConversationScript Vc01McpServeHttpStart => new() {
        Name = "vc01 mcp_serve HTTP 启动",
        Turns =
        [
            new ConversationTurn {
                UserInput = "启动 MCP HTTP 服务器在端口 0",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "MCP HTTP 服务已启动，监听端口 0，token: vc01_mock_token"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vc01: 应有 assistant 回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc01: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc02: mcp_serve token 鉴权 — 请求带错误 token，
    /// 可观测信号 401 Unauthorized。
    /// TODO: 需要真实 mcp_serve HTTP 服务 + HTTP 请求，MockServer 模拟环境下无法构造。
    /// 此脚本用 NonInteractive 模式覆盖调用链路。真实场景需手动 exe 验证（ADR 0080）。
    /// </summary>
    public static ConversationScript Vc02McpServeTokenAuth => new() {
        Name = "vc02 mcp_serve token 鉴权失败",
        Turns =
        [
            new ConversationTurn {
                UserInput = "用错误 token 请求 MCP 服务",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "鉴权失败: 401 Unauthorized，token 无效"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "401", Description = "vc02: 应包含 401 状态码" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vc02: 应有 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc03: mcp_serve 协议版本协商 — MCP-Protocol-Version 头，
    /// 可观测信号 版本匹配/不匹配响应。
    /// TODO: 需要真实 mcp_serve HTTP 服务 + 协议头协商，MockServer 模拟环境下无法构造。
    /// 此脚本用 NonInteractive 模式覆盖调用链路。真实场景需手动 exe 验证（ADR 0080）。
    /// </summary>
    public static ConversationScript Vc03McpServeProtocolVersion => new() {
        Name = "vc03 mcp_serve 协议版本协商",
        Turns =
        [
            new ConversationTurn {
                UserInput = "用 MCP-Protocol-Version 头协商协议版本",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "协议版本协商完成: 2025-11-25 匹配"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "协议版本", Description = "vc03: 应包含协议版本信息" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vc03: 应有 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vc04-vc06: mcp_call 工具调用
    // ============================================================

    /// <summary>
    /// vc04: mcp_call 工具调用成功 — 连接 Mcp.MockServer 并调用 echo 工具，
    /// 可观测信号 工具返回结果 JSON。
    /// 链路: jcc → mcp_connect → Mcp.MockServer → mcp_call_tool(echo) → 返回结果。
    /// </summary>
    public static ConversationScript Vc04McpCallSuccess => new() {
        Name = "vc04 mcp_call 工具调用成功",
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn {
                UserInput = "连接 MCP 服务器",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_connect",
                            Arguments = new { connection_name = "mock_vc04", endpoint = "http://localhost:{MCP_MOCK_PORT}/mcp", transport_type = "http" }
                        }
                    ],
                    FollowUpText = "已连接 MCP 服务器。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "vc04: 应调用 mcp_connect" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc04: 不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "调用 echo 工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_call_tool",
                            Arguments = new { connection_name = "mock_vc04", tool_name = "echo", arguments_json = """{"message":"vc04_success"}""" },
                            ToolResult = "vc04_success"
                        }
                    ],
                    FollowUpText = "echo 工具调用成功，返回: vc04_success"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_call_tool", Description = "vc04: 应调用 mcp_call_tool" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc04: 不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "总结",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "MCP 工具调用成功，echo 返回 vc04_success。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "vc04_success", Description = "vc04: 应包含工具返回结果" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc04: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc05: mcp_call 工具不存在 — 调用不存在的工具，
    /// 可观测信号 "Tool not found" 错误。
    /// 链路: jcc → mcp_connect → Mcp.MockServer → mcp_call_tool(nonexistent) → 错误。
    /// </summary>
    public static ConversationScript Vc05McpCallToolNotFound => new() {
        Name = "vc05 mcp_call 工具不存在",
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn {
                UserInput = "连接 MCP 服务器",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_connect",
                            Arguments = new { connection_name = "mock_vc05", endpoint = "http://localhost:{MCP_MOCK_PORT}/mcp", transport_type = "http" }
                        }
                    ],
                    FollowUpText = "已连接 MCP 服务器。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "vc05: 应调用 mcp_connect" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc05: 连接不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "调用不存在的工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_call_tool",
                            Arguments = new { connection_name = "mock_vc05", tool_name = "nonexistent_tool_vc05", arguments_json = "{}" },
                            ToolResult = "Tool not found: nonexistent_tool_vc05"
                        }
                    ],
                    FollowUpText = "工具不存在: Tool not found"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_call_tool", Description = "vc05: 应调用 mcp_call_tool" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Tool not found", Description = "vc05: 应包含 Tool not found 错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc06: mcp_call 参数校验失败 — 参数不合法，
    /// 可观测信号 参数校验错误诊断。
    /// 链路: jcc → mcp_connect → Mcp.MockServer → mcp_call_tool(非法参数) → 校验错误。
    /// </summary>
    public static ConversationScript Vc06McpCallParamValidationFail => new() {
        Name = "vc06 mcp_call 参数校验失败",
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn {
                UserInput = "连接 MCP 服务器",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_connect",
                            Arguments = new { connection_name = "mock_vc06", endpoint = "http://localhost:{MCP_MOCK_PORT}/mcp", transport_type = "http" }
                        }
                    ],
                    FollowUpText = "已连接 MCP 服务器。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "vc06: 应调用 mcp_connect" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc06: 连接不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "用非法参数调用工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_call_tool",
                            Arguments = new { connection_name = "mock_vc06", tool_name = "echo", arguments_json = "invalid_json_vc06" },
                            ToolResult = "参数校验失败: arguments_json 不是合法 JSON"
                        }
                    ],
                    FollowUpText = "参数校验失败: arguments_json 格式错误"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_call_tool", Description = "vc06: 应调用 mcp_call_tool" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "参数校验失败", Description = "vc06: 应包含参数校验失败诊断" },
                ]
            }
        ]
    };

    // ============================================================
    // vc07-vc09: mcp_list / mcp_schema CLI 子命令
    // ============================================================

    /// <summary>
    /// vc07: mcp_list 列出工具 — jcc mcp_list，
    /// 可观测信号 工具列表 JSON。
    /// TODO: mcp_list 是 CLI 子命令，MockServer 模拟环境下用 NonInteractive 覆盖调用链路。
    /// 真实场景需手动 exe 验证（ADR 0080）: jcc mcp_list。
    /// </summary>
    public static ConversationScript Vc07McpListTools => new() {
        Name = "vc07 mcp_list 列出工具",
        Turns =
        [
            new ConversationTurn {
                UserInput = "列出所有 MCP 工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_list_clients",
                            Arguments = new { },
                            ToolResult = """{"clients":["mock_vc07"],"tools":["echo","add"]}"""
                        }
                    ],
                    FollowUpText = "MCP 工具列表: echo, add"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_list_clients", Description = "vc07: 应调用 mcp_list_clients" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vc07: 应有 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc08: mcp_list 按分类过滤 — jcc mcp_list --category github，
    /// 可观测信号 过滤后工具列表。
    /// TODO: mcp_list --category 是 CLI 子命令，MockServer 模拟环境下用 NonInteractive 覆盖调用链路。
    /// 真实场景需手动 exe 验证（ADR 0080）: jcc mcp_list --category github。
    /// </summary>
    public static ConversationScript Vc08McpListFilterByCategory => new() {
        Name = "vc08 mcp_list 按分类过滤",
        Turns =
        [
            new ConversationTurn {
                UserInput = "列出 github 分类的 MCP 工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_list_clients",
                            Arguments = new { category = "github" },
                            ToolResult = """{"category":"github","tools":["gh_pr_list","gh_issue_list"]}"""
                        }
                    ],
                    FollowUpText = "github 分类工具: gh_pr_list, gh_issue_list"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_list_clients", Description = "vc08: 应调用 mcp_list_clients" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vc08: 应有 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc09: mcp_schema 查 schema — jcc mcp_schema &lt;tool&gt;，
    /// 可观测信号 工具参数 schema JSON。
    /// TODO: mcp_schema 是 CLI 子命令，MockServer 模拟环境下用 NonInteractive 覆盖调用链路。
    /// 真实场景需手动 exe 验证（ADR 0080）: jcc mcp_schema echo。
    /// </summary>
    public static ConversationScript Vc09McpSchemaQuery => new() {
        Name = "vc09 mcp_schema 查 schema",
        Turns =
        [
            new ConversationTurn {
                UserInput = "查看 echo 工具的参数 schema",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = """echo 工具 schema: {"type":"object","properties":{"message":{"type":"string"}},"required":["message"]}"""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "schema", Description = "vc09: 应包含 schema 信息" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vc09: 应有 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vc10-vc13: MCP 连接管理
    // ============================================================

    /// <summary>
    /// vc10: MCP 连接添加 — mcp_connect 工具，
    /// 可观测信号 连接成功 + 持久化。
    /// 链路: jcc → mcp_connect → Mcp.MockServer → 连接成功。
    /// </summary>
    public static ConversationScript Vc10McpConnectAdd => new() {
        Name = "vc10 MCP 连接添加",
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn {
                UserInput = "添加 MCP 连接",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_connect",
                            Arguments = new { connection_name = "mock_vc10", endpoint = "http://localhost:{MCP_MOCK_PORT}/mcp", transport_type = "http" }
                        }
                    ],
                    FollowUpText = "MCP 连接已添加并持久化。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "vc10: 应调用 mcp_connect" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "mcp_connect", Description = "vc10: mcp_connect 应成功" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc10: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc11: MCP 连接移除 — mcp_disconnect 工具，
    /// 可观测信号 连接移除 + 持久化。
    /// 链路: jcc → mcp_connect → mcp_disconnect → 连接移除。
    /// </summary>
    public static ConversationScript Vc11McpDisconnectRemove => new() {
        Name = "vc11 MCP 连接移除",
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn {
                UserInput = "添加 MCP 连接",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_connect",
                            Arguments = new { connection_name = "mock_vc11", endpoint = "http://localhost:{MCP_MOCK_PORT}/mcp", transport_type = "http" }
                        }
                    ],
                    FollowUpText = "MCP 连接已添加。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "vc11: 应先调用 mcp_connect" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc11: 连接不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "移除 MCP 连接",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_disconnect",
                            Arguments = new { connection_name = "mock_vc11" }
                        }
                    ],
                    FollowUpText = "MCP 连接已移除并持久化。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_disconnect", Description = "vc11: 应调用 mcp_disconnect" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc11: 断开不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc12: MCP 连接持久化 — 连接变更后 connections.json 更新，
    /// 可观测信号 连接成功 + 持久化文件更新。
    /// 链路: jcc → mcp_connect → 持久化 → mcp_disconnect → 持久化更新。
    /// </summary>
    public static ConversationScript Vc12McpConnectPersist => new() {
        Name = "vc12 MCP 连接持久化",
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn {
                UserInput = "添加 MCP 连接以测试持久化",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_connect",
                            Arguments = new { connection_name = "mock_vc12", endpoint = "http://localhost:{MCP_MOCK_PORT}/mcp", transport_type = "http" }
                        }
                    ],
                    FollowUpText = "MCP 连接已添加，connections.json 已更新。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "vc12: 应调用 mcp_connect" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "mcp_connect", Description = "vc12: mcp_connect 应成功" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc12: 不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "确认持久化",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "连接已持久化到 connections.json，重启后可恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "持久化", Description = "vc12: 应包含持久化信息" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc12: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc13: MCP 连接恢复 — jcc 启动时从 connections.json 恢复，
    /// 可观测信号 启动后连接可用。
    /// TODO: 需要预先写入 connections.json 再启动 jcc，MockServer 模拟环境下用连接+断开模拟。
    /// 真实场景需手动 exe 验证（ADR 0080）: 预写 connections.json 后启动 jcc。
    /// </summary>
    public static ConversationScript Vc13McpConnectRestore => new() {
        Name = "vc13 MCP 连接恢复",
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn {
                UserInput = "添加 MCP 连接",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_connect",
                            Arguments = new { connection_name = "mock_vc13", endpoint = "http://localhost:{MCP_MOCK_PORT}/mcp", transport_type = "http" }
                        }
                    ],
                    FollowUpText = "MCP 连接已添加并持久化。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "vc13: 应调用 mcp_connect" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc13: 不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "模拟重启后恢复连接",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "连接已从 connections.json 恢复，mock_vc13 可用。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "恢复", Description = "vc13: 应包含恢复信息" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc13: 不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // vc14-vc17: TODO/笔记/记忆工具 (通过 MCP)
    // ============================================================

    /// <summary>
    /// vc14: TODO 工具 (通过 MCP) — MCP 暴露的 TODO 工具，
    /// 可观测信号 TODO 操作结果。
    /// 链路: jcc → mcp_connect → mcp_call_tool(todo_write) → 返回结果。
    /// </summary>
    public static ConversationScript Vc14TodoToolViaMcp => new() {
        Name = "vc14 TODO 工具通过 MCP",
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn {
                UserInput = "连接 MCP 服务器",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_connect",
                            Arguments = new { connection_name = "mock_vc14", endpoint = "http://localhost:{MCP_MOCK_PORT}/mcp", transport_type = "http" }
                        }
                    ],
                    FollowUpText = "已连接 MCP 服务器。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "vc14: 应调用 mcp_connect" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc14: 连接不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "通过 MCP 调用 TODO 工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_call_tool",
                            Arguments = new { connection_name = "mock_vc14", tool_name = "todo_write", arguments_json = """{"todos":[{"content":"vc14 task","status":"pending"}]}""" },
                            ToolResult = "TODO 已创建: vc14 task"
                        }
                    ],
                    FollowUpText = "TODO 工具调用成功，已创建任务。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_call_tool", Description = "vc14: 应调用 mcp_call_tool" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc14: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc15: 笔记工具 (通过 MCP) — MCP 暴露的笔记工具，
    /// 可观测信号 笔记操作结果。
    /// 链路: jcc → mcp_connect → mcp_call_tool(note_write) → 返回结果。
    /// </summary>
    public static ConversationScript Vc15NoteToolViaMcp => new() {
        Name = "vc15 笔记工具通过 MCP",
        RequiresMcpMockServer = true,
        Turns =
        [
            new ConversationTurn {
                UserInput = "连接 MCP 服务器",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_connect",
                            Arguments = new { connection_name = "mock_vc15", endpoint = "http://localhost:{MCP_MOCK_PORT}/mcp", transport_type = "http" }
                        }
                    ],
                    FollowUpText = "已连接 MCP 服务器。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_connect", Description = "vc15: 应调用 mcp_connect" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc15: 连接不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "通过 MCP 调用笔记工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_call_tool",
                            Arguments = new { connection_name = "mock_vc15", tool_name = "note_write", arguments_json = """{"title":"vc15 note","content":"test content"}""" },
                            ToolResult = "笔记已保存: vc15 note"
                        }
                    ],
                    FollowUpText = "笔记工具调用成功，已保存笔记。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_call_tool", Description = "vc15: 应调用 mcp_call_tool" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc15: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc16: 记忆工具保存 — memory_save 工具，
    /// 可观测信号 保存成功。
    /// </summary>
    public static ConversationScript Vc16MemorySave => new() {
        Name = "vc16 记忆工具保存",
        Turns =
        [
            new ConversationTurn {
                UserInput = "保存一条记忆",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "memory_save",
                            Arguments = new { key = "vc16_key", value = "vc16_value", category = "test" },
                            ToolResult = "记忆已保存: vc16_key"
                        }
                    ],
                    FollowUpText = "记忆工具保存成功。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "memory_save", Description = "vc16: 应调用 memory_save" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "memory_save", Description = "vc16: memory_save 应成功" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc16: 不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc17: 记忆工具检索 — memory_retrieve 工具，
    /// 可观测信号 检索结果。
    /// </summary>
    public static ConversationScript Vc17MemoryRetrieve => new() {
        Name = "vc17 记忆工具检索",
        Turns =
        [
            new ConversationTurn {
                UserInput = "检索记忆 vc17_key",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "memory_retrieve",
                            Arguments = new { key = "vc17_key" },
                            ToolResult = "vc17_value"
                        }
                    ],
                    FollowUpText = "记忆检索成功，结果: vc17_value"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "memory_retrieve", Description = "vc17: 应调用 memory_retrieve" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "memory_retrieve", Description = "vc17: memory_retrieve 应成功" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc17: 不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // vc18-vc20: gh / cron 外部工具
    // ============================================================

    /// <summary>
    /// vc18: gh 工具 pr list — jcc gh pr list，
    /// 可观测信号 PR 列表。
    /// TODO: gh 是 CLI 子命令，MockServer 模拟环境下用工具调用覆盖链路。
    /// 真实场景需手动 exe 验证（ADR 0080）: jcc gh pr list。
    /// </summary>
    public static ConversationScript Vc18GhPrList => new() {
        Name = "vc18 gh 工具 pr list",
        Turns =
        [
            new ConversationTurn {
                UserInput = "列出 GitHub PR",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "gh_pr_list",
                            Arguments = new { limit = 5 },
                            ToolResult = """[{"number":1,"title":"vc18 PR","state":"open"}]"""
                        }
                    ],
                    FollowUpText = "PR 列表: #1 vc18 PR (open)"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "gh_pr_list", Description = "vc18: 应调用 gh_pr_list" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vc18: 应有 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc19: gh 工具 issue — jcc gh issue list，
    /// 可观测信号 Issue 列表。
    /// TODO: gh 是 CLI 子命令，MockServer 模拟环境下用工具调用覆盖链路。
    /// 真实场景需手动 exe 验证（ADR 0080）: jcc gh issue list。
    /// </summary>
    public static ConversationScript Vc19GhIssueList => new() {
        Name = "vc19 gh 工具 issue list",
        Turns =
        [
            new ConversationTurn {
                UserInput = "列出 GitHub Issue",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "gh_issue_list",
                            Arguments = new { limit = 5 },
                            ToolResult = """[{"number":1,"title":"vc19 Issue","state":"open"}]"""
                        }
                    ],
                    FollowUpText = "Issue 列表: #1 vc19 Issue (open)"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "gh_issue_list", Description = "vc19: 应调用 gh_issue_list" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vc19: 应有 assistant 回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc20: cron 工具创建定时任务 — cron_create 工具，
    /// 可观测信号 定时任务创建成功。
    /// </summary>
    public static ConversationScript Vc20CronCreate => new() {
        Name = "vc20 cron 工具创建定时任务",
        Turns =
        [
            new ConversationTurn {
                UserInput = "创建一个定时任务",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "cron_create",
                            Arguments = new { cron = "0 9 * * *", prompt = "vc20 daily task", recurring = true },
                            ToolResult = "定时任务已创建: id=vc20_task"
                        }
                    ],
                    FollowUpText = "cron 工具创建定时任务成功。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "cron_create", Description = "vc20: 应调用 cron_create" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "cron_create", Description = "vc20: cron_create 应成功" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "vc20: 不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // vc21: MCP 权限拒绝
    // ============================================================

    /// <summary>
    /// vc21: MCP 权限拒绝 — 权限模式拦截 MCP 工具，
    /// 可观测信号 permission_denials 记录。
    /// TODO: 需要权限模式环境（JCC_PERMISSION_MODE=strict），MockServer 模拟环境下用 ExtraEnvVars 设置。
    /// 真实场景需手动 exe 验证（ADR 0080）: 设置权限模式后调用受限 MCP 工具。
    /// </summary>
    public static ConversationScript Vc21McpPermissionDenied => new() {
        Name = "vc21 MCP 权限拒绝",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_PERMISSION_MODE"] = "strict"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "调用受限 MCP 工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_call_tool",
                            Arguments = new { connection_name = "mock_vc21", tool_name = "restricted_tool", arguments_json = "{}" },
                            ToolResult = "权限拒绝: permission_denials 已记录"
                        }
                    ],
                    FollowUpText = "MCP 工具调用被权限模式拦截。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_call_tool", Description = "vc21: 应调用 mcp_call_tool" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "权限拒绝", Description = "vc21: 应包含权限拒绝诊断" },
                ]
            }
        ]
    };

    // ============================================================
    // vc22: MCPB 包加载
    // ============================================================

    /// <summary>
    /// vc22: MCPB 包加载 — 加载 MCPB 包，
    /// 可观测信号 包加载成功/失败。
    /// TODO: 需要真实 MCPB 包文件，MockServer 模拟环境下用工具调用覆盖链路。
    /// 真实场景需手动 exe 验证（ADR 0080）: jcc mcp_load_package &lt;path&gt;。
    /// </summary>
    public static ConversationScript Vc22McpbPackageLoad => new() {
        Name = "vc22 MCPB 包加载",
        Turns =
        [
            new ConversationTurn {
                UserInput = "加载 MCPB 包",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "mcp_load_package",
                            Arguments = new { package_path = "vc22_mock.mcpb" },
                            ToolResult = "MCPB 包加载成功: vc22_mock"
                        }
                    ],
                    FollowUpText = "MCPB 包加载成功。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_load_package", Description = "vc22: 应调用 mcp_load_package" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "vc22: 应有 assistant 回复" },
                ]
            }
        ]
    };

    // ============================================================
    // vc23-vc25: ToolInterventionManager
    // ============================================================

    /// <summary>
    /// vc23: ToolInterventionManager 黑名单 — 工具在黑名单中，
    /// 可观测信号 工具被拦截。
    /// TODO: 需要 ToolInterventionManager 黑名单配置，MockServer 模拟环境下用 ExtraEnvVars 设置。
    /// 真实场景需手动 exe 验证（ADR 0080）: 配置黑名单后调用被禁工具。
    /// </summary>
    public static ConversationScript Vc23ToolInterventionBlacklist => new() {
        Name = "vc23 ToolInterventionManager 黑名单",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_TOOL_BLACKLIST"] = "bash"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "调用被黑名单拦截的工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "echo vc23" },
                            ToolResult = "工具被黑名单拦截: bash"
                        }
                    ],
                    FollowUpText = "工具调用被 ToolInterventionManager 黑名单拦截。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "vc23: 应包含 bash 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "拦截", Description = "vc23: 应包含拦截诊断" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc24: ToolInterventionManager 降权 — 工具被降权，
    /// 可观测信号 工具以降权模式执行。
    /// TODO: 需要 ToolInterventionManager 降权配置，MockServer 模拟环境下用 ExtraEnvVars 设置。
    /// 真实场景需手动 exe 验证（ADR 0080）: 配置降权后调用工具。
    /// </summary>
    public static ConversationScript Vc24ToolInterventionDowngrade => new() {
        Name = "vc24 ToolInterventionManager 降权",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_TOOL_DOWNGRADE"] = "bash"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "调用被降权的工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "echo vc24" },
                            ToolResult = "工具以降权模式执行: bash (readonly)"
                        }
                    ],
                    FollowUpText = "工具调用以降权模式执行。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "vc24: 应包含 bash 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "降权", Description = "vc24: 应包含降权诊断" },
                ]
            }
        ]
    };

    /// <summary>
    /// vc25: ToolInterventionManager 重定向 — 工具被重定向，
    /// 可观测信号 工具重定向到替代实现。
    /// TODO: 需要 ToolInterventionManager 重定向配置，MockServer 模拟环境下用 ExtraEnvVars 设置。
    /// 真实场景需手动 exe 验证（ADR 0080）: 配置重定向后调用工具。
    /// </summary>
    public static ConversationScript Vc25ToolInterventionRedirect => new() {
        Name = "vc25 ToolInterventionManager 重定向",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_TOOL_REDIRECT"] = "bash->bash_safe"
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "调用被重定向的工具",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript {
                            ToolName = "bash",
                            Arguments = new { command = "echo vc25" },
                            ToolResult = "工具重定向到替代实现: bash -> bash_safe"
                        }
                    ],
                    FollowUpText = "工具调用重定向到替代实现。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "bash", Description = "vc25: 应包含 bash 工具调用" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "重定向", Description = "vc25: 应包含重定向诊断" },
                ]
            }
        ]
    };
}
