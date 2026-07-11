using System;

namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// 更多任务工具和调度工具 E2E 测试脚本
/// </summary>
public static class ExtendedToolScripts
{
    // ============================================================
    // TaskList 工具
    // ============================================================
    public static ConversationScript TaskListTest => new()
    {
        Name = "TaskList 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "列出当前任务",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "TaskList",
                            Arguments = "{}"
                        }
                    ],
                    FollowUpText = "当前没有任务。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "TaskList", Description = "应包含TaskList工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // TaskGet 工具
    // ============================================================
    public static ConversationScript TaskGetTest => new()
    {
        Name = "TaskGet 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看任务详情",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "TaskGet",
                            Arguments = """{"task_id":"test-task-001"}"""
                        }
                    ],
                    FollowUpText = "任务不存在。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "TaskGet", Description = "应包含TaskGet工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // TaskStop 工具
    // ============================================================
    public static ConversationScript TaskStopTest => new()
    {
        Name = "TaskStop 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "停止一个任务",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "TaskStop",
                            Arguments = """{"task_id":"test-task-001"}"""
                        }
                    ],
                    FollowUpText = "任务已停止。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "TaskStop", Description = "应包含TaskStop工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // TaskUpdate 工具
    // ============================================================
    public static ConversationScript TaskUpdateTest => new()
    {
        Name = "TaskUpdate 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "更新任务状态",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "TaskUpdate",
                            Arguments = """{"task_id":"test-task-001","status":"completed"}"""
                        }
                    ],
                    FollowUpText = "任务状态已更新为 completed。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "TaskUpdate", Description = "应包含TaskUpdate工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // CronCreate 工具
    // ============================================================
    public static ConversationScript CronCreateTest => new()
    {
        Name = "CronCreate 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "创建一个定时任务",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "CronCreate",
                            Arguments = """{"name":"test-cron","schedule":"*/5 * * * *","prompt":"定期检查","timezone":"UTC"}"""
                        }
                    ],
                    FollowUpText = "定时任务 'test-cron' 已创建。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "CronCreate", Description = "应包含CronCreate工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // CronDelete 工具
    // ============================================================
    public static ConversationScript CronDeleteTest => new()
    {
        Name = "CronDelete 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "删除一个定时任务",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "CronDelete",
                            Arguments = """{"task_id":"test-cron-001"}"""
                        }
                    ],
                    FollowUpText = "定时任务已删除。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "CronDelete", Description = "应包含CronDelete工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // AskUserQuestion 工具
    // ============================================================
    public static ConversationScript AskUserQuestionTest => new()
    {
        Name = "AskUserQuestion 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "问我一个问题",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "AskUserQuestion",
                            Arguments = """{"question":"你确定要删除这个文件吗？","options":[{"label":"是","description":"确认删除"},{"label":"否","description":"取消操作"}]}"""
                        }
                    ],
                    FollowUpText = "等待你的回答..."
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "AskUserQuestion", Description = "应包含AskUserQuestion工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // web_fetch 工具 — SSRF 防护验证
    // ============================================================
    public static ConversationScript WebFetchToolCall => new()
    {
        Name = "web_fetch 工具调用（SSRF 防护）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "获取网络资源",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "web_fetch",
                            Arguments = """{"url":"https://example.com/data.json"}"""
                        }
                    ],
                    FollowUpText = "网络资源获取完成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "web_fetch", Description = "应包含web_fetch工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    // ============================================================
    // complete_step 工具
    // ============================================================
    public static ConversationScript CompleteStepToolTest => new()
    {
        Name = "complete_step 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "完成步骤一",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "complete_step",
                            Arguments = """{"step":"1. 初始化","result":"初始化完成","evidence":[{"kind":"auto","summary":"系统就绪"}]}"""
                        }
                    ],
                    FollowUpText = "步骤一已完成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "complete_step", Description = "应包含complete_step" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    // ============================================================
    // TaskOutput 工具 — 获取后台任务输出
    // ============================================================
    public static ConversationScript TaskOutputTest => new()
    {
        Name = "TaskOutput 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看任务输出",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "TaskOutput",
                            Arguments = """{"task_id":"test-task-001","output_type":"all","max_lines":100}"""
                        }
                    ],
                    FollowUpText = "任务 test-task-001 不存在或无输出。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "TaskOutput", Description = "应包含TaskOutput工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}