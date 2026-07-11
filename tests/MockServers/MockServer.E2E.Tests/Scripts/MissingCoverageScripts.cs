using System;

namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// 缺失覆盖的功能 E2E 测试脚本
/// Write / Edit / Grep / Glob 工具 + Agent spawn + 聊天命令
/// </summary>
public static class MissingCoverageScripts
{
    // ============================================================
    // Write 工具调用
    // ============================================================
    public static ConversationScript WriteToolCall => new()
    {
        Name = "Write工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "创建一个HelloWorld文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "文件已创建",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Write",
                            Arguments = """{"file_path":"test_hello.txt","content":"Hello, World!"}"""
                        }
                    ],
                    FollowUpText = "文件 test_hello.txt 已创建，内容为 Hello, World!"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Write", Description = "应包含Write工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    // ============================================================
    // Edit 工具调用 — 编辑现有文件
    // ============================================================
    public static ConversationScript EditToolCall => new()
    {
        Name = "Edit工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "编辑配置文件，修改版本号",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "文件已更新",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Edit",
                            Arguments = """{"file_path":"test_config.txt","old_string":"version: 1.0","new_string":"version: 2.0"}"""
                        }
                    ],
                    FollowUpText = "配置文件版本号已从 1.0 更新为 2.0"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Edit", Description = "应包含Edit工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    // ============================================================
    // Grep 工具调用 — 搜索文件内容
    // ============================================================
    public static ConversationScript GrepToolCall => new()
    {
        Name = "Grep工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "搜索所有cs文件中的TODO标记",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Grep",
                            Arguments = """{"pattern":"TODO","include":"*.cs"}"""
                        }
                    ],
                    FollowUpText = "在 3 个文件中找到 5 个 TODO 标记"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Grep", Description = "应包含Grep工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    // ============================================================
    // Glob 工具调用 — 文件通配
    // ============================================================
    public static ConversationScript GlobToolCall => new()
    {
        Name = "Glob工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查找所有JSON配置文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Glob",
                            Arguments = """{"pattern":"**/*.json"}"""
                        }
                    ],
                    FollowUpText = "找到 12 个 JSON 配置文件"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Glob", Description = "应包含Glob工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    // ============================================================
    // Agent spawn 全链路测试
    // ============================================================
    public static ConversationScript AgentSpawnToolCall => new()
    {
        Name = "Agent工具调用（spawn 子代理）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我检查项目中的README文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Agent",
                            Arguments = """{"name":"check-readme","description":"检查README","prompt":"检查当前目录下README文件，返回其内容概要"}"""
                        }
                    ],
                    // Agent spawn 不需要 follow_up — subagent 的输出通过 ToolResult 返回
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Agent", Description = "应包含Agent工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复（含Agent输出）" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ],
        // 子代理需要额外的 MockServer 轮次（subagent 自己的 LLM 调用）
        MockServerExtraTextResponses =
        [
            "README 文件存在。内容概要：JoinCode 项目是一个 AI 工作流引擎，支持多种 LLM 供应商。",
            "任务完成。输出了README文件概要。"
        ]
    };

    // ============================================================
    // Agent 通过 agent_spawn 工具 spawn
    // ============================================================
    public static ConversationScript AgentSpawnViaTool => new()
    {
        Name = "agent_spawn 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我搜索代码中的类定义",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "agent_spawn",
                            Arguments = """{"name":"code-search","description":"搜索代码","prompt":"搜索代码中的所有类定义，列出5个主要类名"}"""
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "agent_spawn", Description = "应包含agent_spawn工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "找到以下类：ChatService, PlanService, ToolRegistry, SessionAdmin, CliSession",
            "类名已列表返回。"
        ]
    };

    // ============================================================
    // Agent spawn with worktree isolation
    // ============================================================
    public static ConversationScript AgentWorktreeIsolation => new()
    {
        Name = "Agent工具调用（worktree隔离）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "单独检查README文件的拼写问题",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Agent",
                            Arguments = """{"name":"spell-check","description":"拼写检查","prompt":"检查README.md中的拼写问题","isolation":"worktree"}"""
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Agent", Description = "应包含Agent工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复（含Agent输出）" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ],
        MockServerExtraTextResponses =
        [
            "检查完成，README.md中没有拼写问题，格式正确。",
            "任务完成。输出了拼写检查结果。"
        ]
    };
}

/// <summary>
/// 聊天命令 E2E 测试脚本
/// </summary>
public static class ChatCommandScripts
{
    // ============================================================
    // /help 命令
    // ============================================================
    public static ConversationScript HelpCommand => new()
    {
        Name = "/help 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/help",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "以下是可用命令列表：\n/help - 显示帮助\n/clear - 清除对话\n/config - 配置管理\n/model - 切换模型\n/exit - 退出\n更多命令请查看文档。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "/help", Description = "输出应包含/help命令" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "/clear", Description = "输出应包含/clear命令" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // /clear 命令
    // ============================================================
    public static ConversationScript ClearCommand => new()
    {
        Name = "/clear 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/clear",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "对话已清除。有什么我可以帮你的？"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}

/// <summary>
/// 任务工具 E2E 测试脚本
/// </summary>
public static class TaskToolScripts
{
    public static ConversationScript TaskCreateTest => new()
    {
        Name = "TaskCreate 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "创建一个新任务",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "TaskCreate",
                            Arguments = """{"title":"测试任务","description":"这是一个E2E测试任务"}"""
                        }
                    ],
                    FollowUpText = "任务『测试任务』已创建。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "TaskCreate", Description = "应包含TaskCreate工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// 基础设施工具 E2E 测试脚本
/// </summary>
public static class InfrastructureToolScripts
{
    public static ConversationScript StructuredOutputRegisterTest => new()
    {
        Name = "structured_output_register 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "注册一个输出Schema",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "structured_output_register",
                            Arguments = """{"schema_name":"test_schema","schema_json":"{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}","description":"测试Schema"}"""
                        }
                    ],
                    FollowUpText = "Schema 'test_schema' 已注册。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "structured_output_register", Description = "应包含structured_output_register工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// 调度工具 E2E 测试脚本
/// </summary>
public static class SchedulingToolScripts
{
    public static ConversationScript CronListTest => new()
    {
        Name = "CronList 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "列出所有定时任务",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "CronList",
                            Arguments = "{}"
                        }
                    ],
                    FollowUpText = "当前没有定时任务。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "CronList", Description = "应包含CronList工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// 网络工具 E2E 测试脚本
/// </summary>
public static class WebToolScripts
{
    public static ConversationScript WebSearchTest => new()
    {
        Name = "WebSearch 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "搜索JoinCode相关信息",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "WebSearch",
                            Arguments = """{"query":"JoinCode AI workflow engine"}"""
                        }
                    ],
                    FollowUpText = "搜索结果：JoinCode是一个AI工作流引擎..."
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "WebSearch", Description = "应包含WebSearch工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}