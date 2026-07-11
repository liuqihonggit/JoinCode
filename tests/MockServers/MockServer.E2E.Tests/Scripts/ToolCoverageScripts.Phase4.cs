namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// Phase 4 E2E 脚本 — 补齐未覆盖工具类别
/// 覆盖 20 个工具类别: pr_subscription/peers/vcr/terminal/REPL/snip/monitor/context/
/// analytics/browser/policy/mcp_resource/code_execution/lsp/code_generation/
/// code_analysis/permission/voice/remote_trigger/notification
/// </summary>
public static class ToolCoverageScripts
{
    // ============================================================
    // 4a 必选工具
    // ============================================================

    /// <summary>
    /// mcp_resource 类别 — mcp_list_clients 工具
    /// 列出已连接的 MCP 客户端
    /// </summary>
    public static ConversationScript McpListClientsTest => new()
    {
        Name = "mcp_list_clients 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "列出已连接的MCP客户端",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "mcp_list_clients",
                            Arguments = "{}"
                        }
                    ],
                    FollowUpText = "当前没有已连接的 MCP 客户端。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_list_clients", Description = "应包含mcp_list_clients工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// code_execution 类别 — execute_csharp_code 工具
    /// 执行 C# 代码片段
    /// </summary>
    public static ConversationScript ExecuteCsharpCodeTest => new()
    {
        Name = "execute_csharp_code 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "执行一段C#代码打印hello",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "execute_csharp_code",
                            Arguments = """{"code":"Console.WriteLine(\"hello\");"}"""
                        }
                    ],
                    FollowUpText = "代码已执行,输出: hello"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "execute_csharp_code", Description = "应包含execute_csharp_code工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // 4b 可选工具集合 1（dev 类）
    // ============================================================

    /// <summary>
    /// snip 类别 — snip 工具
    /// 历史快照回退
    /// </summary>
    public static ConversationScript SnipTest => new()
    {
        Name = "snip 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "回退对话历史",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "snip",
                            Arguments = """{"mode":"rewind"}"""
                        }
                    ],
                    FollowUpText = "已执行历史回退操作。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "snip", Description = "应包含snip工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// monitor 类别 — monitor 工具
    /// MCP 监控状态
    /// </summary>
    public static ConversationScript MonitorTest => new()
    {
        Name = "monitor 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看MCP监控状态",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "monitor",
                            Arguments = """{"monitor_type":"status"}"""
                        }
                    ],
                    FollowUpText = "MCP 监控状态已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "monitor", Description = "应包含monitor工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// context 类别 — ctx_inspect 工具
    /// 上下文检查
    /// </summary>
    public static ConversationScript CtxInspectTest => new()
    {
        Name = "ctx_inspect 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "检查当前上下文",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "ctx_inspect",
                            Arguments = """{"inspect_type":"summary"}"""
                        }
                    ],
                    FollowUpText = "上下文摘要已生成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "ctx_inspect", Description = "应包含ctx_inspect工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// analytics 类别 — analytics_report 工具
    /// 分析报告
    /// </summary>
    public static ConversationScript AnalyticsReportTest => new()
    {
        Name = "analytics_report 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "生成分析报告",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "analytics_report",
                            Arguments = """{"days":7}"""
                        }
                    ],
                    FollowUpText = "已生成 7 天的分析报告。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "analytics_report", Description = "应包含analytics_report工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// policy 类别 — policy_list 工具
    /// 策略列表
    /// </summary>
    public static ConversationScript PolicyListTest => new()
    {
        Name = "policy_list 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "列出策略规则",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "policy_list",
                            Arguments = "{}"
                        }
                    ],
                    FollowUpText = "策略列表已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "policy_list", Description = "应包含policy_list工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // 4c 可选工具集合 2（code 类）
    // ============================================================

    /// <summary>
    /// lsp 类别 — lsp_document_symbols 工具
    /// LSP 文档符号
    /// </summary>
    public static ConversationScript LspDocumentSymbolsTest => new()
    {
        Name = "lsp_document_symbols 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "获取文件符号列表",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "lsp_document_symbols",
                            Arguments = """{"file_path":"Program.cs"}"""
                        }
                    ],
                    FollowUpText = "文档符号已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "lsp_document_symbols", Description = "应包含lsp_document_symbols工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// code_generation 类别 — generate_csharp_code 工具
    /// 生成 C# 代码
    /// </summary>
    public static ConversationScript GenerateCsharpCodeTest => new()
    {
        Name = "generate_csharp_code 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "生成一个工具类",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "generate_csharp_code",
                            Arguments = """{"description":"生成一个字符串工具类"}"""
                        }
                    ],
                    FollowUpText = "已生成 C# 代码。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "generate_csharp_code", Description = "应包含generate_csharp_code工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// code_analysis 类别 — analyze_csharp_code 工具
    /// 分析 C# 代码
    /// </summary>
    public static ConversationScript AnalyzeCsharpCodeTest => new()
    {
        Name = "analyze_csharp_code 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "分析这段代码",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "analyze_csharp_code",
                            Arguments = """{"code":"public class Foo {}","focus":"all"}"""
                        }
                    ],
                    FollowUpText = "代码分析完成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "analyze_csharp_code", Description = "应包含analyze_csharp_code工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // 4d 可选工具集合 3（terminal/REPL/vcr 类）
    // ============================================================

    /// <summary>
    /// vcr 类别 — vcr_status 工具
    /// VCR 状态查询
    /// </summary>
    public static ConversationScript VcrStatusTest => new()
    {
        Name = "vcr_status 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看VCR状态",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "vcr_status",
                            Arguments = "{}"
                        }
                    ],
                    FollowUpText = "VCR 状态已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "vcr_status", Description = "应包含vcr_status工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// terminal 类别 — terminal_capture 工具
    /// 终端截图
    /// </summary>
    public static ConversationScript TerminalCaptureTest => new()
    {
        Name = "terminal_capture 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "截取终端画面",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "terminal_capture",
                            Arguments = """{"capture_type":"screen","max_lines":50}"""
                        }
                    ],
                    FollowUpText = "终端画面已截取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "terminal_capture", Description = "应包含terminal_capture工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// REPL 类别 — REPL 工具
    /// REPL 状态查询
    /// </summary>
    public static ConversationScript ReplTest => new()
    {
        Name = "REPL 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看REPL状态",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "REPL",
                            Arguments = """{"action":"status"}"""
                        }
                    ],
                    FollowUpText = "REPL 状态已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "REPL", Description = "应包含REPL工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // 4e 可选工具集合 4（user/communication 类）
    // ============================================================

    /// <summary>
    /// pr_subscription 类别 — subscribe_pr 工具
    /// PR 订阅列表
    /// </summary>
    public static ConversationScript SubscribePrTest => new()
    {
        Name = "subscribe_pr 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "列出PR订阅",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "subscribe_pr",
                            Arguments = """{"action":"list"}"""
                        }
                    ],
                    FollowUpText = "PR 订阅列表已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "subscribe_pr", Description = "应包含subscribe_pr工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// peers 类别 — list_peers 工具
    /// 列出对等节点
    /// </summary>
    public static ConversationScript ListPeersTest => new()
    {
        Name = "list_peers 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "列出对等节点",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "list_peers",
                            Arguments = """{"filter":"all"}"""
                        }
                    ],
                    FollowUpText = "对等节点列表已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "list_peers", Description = "应包含list_peers工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// browser 类别 — web_browser 工具
    /// 浏览器打开页面
    /// </summary>
    public static ConversationScript WebBrowserTest => new()
    {
        Name = "web_browser 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "打开网页",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "web_browser",
                            Arguments = """{"target":"https://example.com","action":"open"}"""
                        }
                    ],
                    FollowUpText = "网页已打开。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "web_browser", Description = "应包含web_browser工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// voice 类别 — voice_status 工具
    /// 语音服务状态
    /// </summary>
    public static ConversationScript VoiceStatusTest => new()
    {
        Name = "voice_status 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看语音状态",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "voice_status",
                            Arguments = "{}"
                        }
                    ],
                    FollowUpText = "语音服务状态已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "voice_status", Description = "应包含voice_status工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// remote_trigger 类别 — RemoteTrigger 工具
    /// 远程触发器列表
    /// </summary>
    public static ConversationScript RemoteTriggerTest => new()
    {
        Name = "RemoteTrigger 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "列出远程触发器",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "RemoteTrigger",
                            Arguments = """{"action":"list"}"""
                        }
                    ],
                    FollowUpText = "远程触发器列表已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "RemoteTrigger", Description = "应包含RemoteTrigger工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// notification 类别 — push_notification 工具
    /// 推送通知
    /// </summary>
    public static ConversationScript PushNotificationTest => new()
    {
        Name = "push_notification 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "发送通知",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "push_notification",
                            Arguments = """{"title":"测试","message":"你好","level":"info"}"""
                        }
                    ],
                    FollowUpText = "通知已发送。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "push_notification", Description = "应包含push_notification工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // 4f permission 工具
    // ============================================================

    /// <summary>
    /// permission 类别 — permission_list_rules 工具
    /// 权限规则列表
    /// </summary>
    public static ConversationScript PermissionListRulesTest => new()
    {
        Name = "permission_list_rules 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "列出权限规则",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "permission_list_rules",
                            Arguments = "{}"
                        }
                    ],
                    FollowUpText = "权限规则列表已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "permission_list_rules", Description = "应包含permission_list_rules工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}
