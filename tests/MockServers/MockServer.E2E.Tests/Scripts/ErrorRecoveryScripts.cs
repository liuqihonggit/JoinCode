namespace MockServer.E2E.Tests.Scripts;

public static class ApiErrorRecoveryScripts
{
    public static ConversationScript RateLimit429ThenRecover => new()
    {
        Name = "429限流后恢复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "服务器繁忙，请稍后再试。",
                    HttpStatusCode = 429
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "429后应有恢复回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！现在可以正常对话了。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "重试后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "恢复后不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript ServerError500ThenRecover => new()
    {
        Name = "500服务器错误后恢复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Internal Server Error",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "500后应有恢复回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！服务器已恢复正常。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "重试后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "恢复后不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript ServiceUnavailable503ThenRecover => new()
    {
        Name = "503服务不可用后恢复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Service Unavailable",
                    HttpStatusCode = 503
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "503后应有恢复回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！服务已恢复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "重试后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "恢复后不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript ErrorThenToolCallThenRecover => new()
    {
        Name = "错误后工具调用恢复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看目录",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Server Error",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "500后应有恢复回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "再试一次查看目录",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "当前目录为 /home/user/project",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"pwd\"}"
                        }
                    ],
                    FollowUpText = "当前目录为 /home/user/project"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "恢复后应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "恢复后应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript AuthError401NoRetry => new()
    {
        Name = "401认证错误不重试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Unauthorized",
                    HttpStatusCode = 401
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "401后应有错误提示" },
                ]
            }
        ]
    };
}

public static class StreamInterruptionScripts
{
    public static ConversationScript ToolCallFailureThenRecover => new()
    {
        Name = "工具调用失败后恢复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "读取不存在的文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "文件不存在，读取失败。让我尝试其他方式。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Read",
                            Arguments = "{\"file_path\":\"/nonexistent/file.txt\"}"
                        }
                    ],
                    FollowUpText = "文件不存在，读取失败。让我尝试其他方式。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应调用ReadFile" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "失败后应有恢复回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "换一个存在的文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "好的，找到了存在的文件。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "恢复后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "恢复后不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript UnknownToolThenFallback => new()
    {
        Name = "未知工具后降级回复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "使用特殊工具",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "该工具不可用，我将用其他方式完成。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "NonExistentTool",
                            Arguments = "{\"param\":\"value\"}"
                        }
                    ],
                    FollowUpText = "该工具不可用，我将用其他方式完成。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "NonExistentTool", Description = "应包含工具调用" },
                    new OutputAssert { Type = AssertType.ToolCallFailed, Expected = "NonExistentTool", Description = "未知工具应失败" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "失败后应有降级回复" },
                ]
            }
        ]
    };

    public static ConversationScript MultiToolPartialFailure => new()
    {
        Name = "多工具部分失败",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看目录和读取文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "目录查看成功，但文件读取失败。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"ls\"}"
                        },
                        new MockToolCallScript
                        {
                            ToolName = "Read",
                            Arguments = "{\"file_path\":\"/nonexistent.txt\"}"
                        }
                    ],
                    FollowUpText = "目录查看成功，但文件读取失败。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应包含ReadFile工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "部分失败后应有回复" },
                ]
            }
        ]
    };
}

public static class PermissionDenialScripts
{
    public static ConversationScript AskPermissionMode => new()
    {
        Name = "ask权限模式对话",
        Mode = ConversationMode.Interactive,
        ExtraEnvVars = new Dictionary<string, string>
        {
            ["JCC_PERMISSION_MODE"] = "ask"
        },
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！有什么可以帮你的？"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "ask模式下纯文本应有回复" },
                ]
            }
        ]
    };

    public static ConversationScript DenyPermissionMode => new()
    {
        Name = "deny权限模式对话",
        Mode = ConversationMode.Interactive,
        ExtraEnvVars = new Dictionary<string, string>
        {
            ["JCC_PERMISSION_MODE"] = "deny"
        },
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！有什么可以帮你的？"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "deny模式下纯文本应有回复" },
                ]
            }
        ]
    };

    public static ConversationScript DenyModeToolCallBlocked => new()
    {
        Name = "deny模式下工具调用被阻止",
        Mode = ConversationMode.Interactive,
        ExtraEnvVars = new Dictionary<string, string>
        {
            ["JCC_PERMISSION_MODE"] = "deny"
        },
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看当前目录",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "权限被拒绝，无法执行该操作。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"pwd\"}"
                        }
                    ],
                    FollowUpText = "权限被拒绝，无法执行该操作。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ToolCallFailed, Expected = "Bash", Description = "deny模式下Bash工具调用应失败" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "权限拒绝后应有回复" },
                ]
            }
        ]
    };

    public static ConversationScript AutoPermissionModeToolCall => new()
    {
        Name = "auto权限模式工具调用",
        Mode = ConversationMode.Interactive,
        ExtraEnvVars = new Dictionary<string, string>
        {
            ["JCC_PERMISSION_MODE"] = "auto"
        },
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看当前目录",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "当前目录为 /home/user/project",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"pwd\"}"
                        }
                    ],
                    FollowUpText = "当前目录为 /home/user/project"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "auto模式应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "auto模式应有助手回复" },
                ]
            }
        ]
    };
}

public static class AnthropicDeepCoverageScripts
{
    public static ConversationScript AnthropicMultiToolCalls => new()
    {
        Name = "Anthropic多工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看目录并读取README",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "目录下有 README.md，内容为项目说明。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"ls\"}"
                        },
                        new MockToolCallScript
                        {
                            ToolName = "Read",
                            Arguments = "{\"file_path\":\"README.md\"}"
                        }
                    ],
                    FollowUpText = "目录下有 README.md，内容为项目说明。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应包含ReadFile工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript AnthropicThinkingThenToolCall => new()
    {
        Name = "Anthropic思考后工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "分析当前目录结构",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    ThinkingContent = "用户想了解目录结构，我需要先查看目录内容。",
                    TextResponse = "当前目录包含 src、tests、docs 等子目录。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"ls -la\"}"
                        }
                    ],
                    FollowUpText = "当前目录包含 src、tests、docs 等子目录。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "思考后应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript AnthropicFiveRoundMemory => new()
    {
        Name = "Anthropic五轮对话记忆",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "请记住我的名字是李四",
                AiResponse = new MockResponseScript { TextResponse = "好的，我记住了，你叫李四。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我在做后端开发",
                AiResponse = new MockResponseScript { TextResponse = "了解了，你在做后端开发。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我叫什么名字？",
                AiResponse = new MockResponseScript { TextResponse = "你叫李四。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮应记住名字" }]
            },
            new ConversationTurn
            {
                UserInput = "我在做什么？",
                AiResponse = new MockResponseScript { TextResponse = "你是李四，你在做后端开发。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第4轮应记住上下文" }]
            },
            new ConversationTurn
            {
                UserInput = "总结一下我们的对话",
                AiResponse = new MockResponseScript { TextResponse = "你是李四，一名后端开发者。我们讨论了你的身份和工作。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第5轮总结应有回复" }]
            }
        ]
    };

    public static ConversationScript DeepSeekReasoningThenToolCall => new()
    {
        Name = "DeepSeek推理后工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我查看项目结构",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    ThinkingContent = "用户想查看项目结构，我需要用ls命令列出目录内容。",
                    TextResponse = "项目结构如下：包含src、tests、docs目录。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"ls\"}"
                        }
                    ],
                    FollowUpText = "项目结构如下：包含src、tests、docs目录。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "推理后应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript DeepSeekMultiToolCalls => new()
    {
        Name = "DeepSeek多工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看目录并读取配置文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "目录下有配置文件，内容为项目配置。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"ls\"}"
                        },
                        new MockToolCallScript
                        {
                            ToolName = "Read",
                            Arguments = "{\"file_path\":\"config.json\"}"
                        }
                    ],
                    FollowUpText = "目录下有配置文件，内容为项目配置。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应包含ReadFile工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript DeepSeekFiveRoundMemory => new()
    {
        Name = "DeepSeek五轮对话记忆",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "请记住我的名字是王五",
                AiResponse = new MockResponseScript { TextResponse = "好的，我记住了，你叫王五。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我在做前端开发",
                AiResponse = new MockResponseScript { TextResponse = "了解了，你在做前端开发。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我叫什么名字？",
                AiResponse = new MockResponseScript { TextResponse = "你叫王五。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮应记住名字" }]
            },
            new ConversationTurn
            {
                UserInput = "我在做什么？",
                AiResponse = new MockResponseScript { TextResponse = "你是王五，你在做前端开发。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第4轮应记住上下文" }]
            },
            new ConversationTurn
            {
                UserInput = "总结一下",
                AiResponse = new MockResponseScript { TextResponse = "你是王五，一名前端开发者。我们讨论了你的身份和工作。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第5轮总结应有回复" }]
            }
        ]
    };

    public static ConversationScript AnthropicError429ThenRecover => new()
    {
        Name = "Anthropic 429限流后恢复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Rate limit exceeded",
                    HttpStatusCode = 429
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "429后应有恢复回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！现在可以正常对话了。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "重试后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "恢复后不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript DeepSeekError500ThenRecover => new()
    {
        Name = "DeepSeek 500服务器错误后恢复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Internal Server Error",
                    HttpStatusCode = 500
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "500后应有恢复回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "再试一次",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！服务器已恢复正常。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "重试后应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "恢复后不应有错误" },
                ]
            }
        ]
    };
}

public static class McpProtocolScripts
{
    public static ConversationScript McpToolListThenCall => new()
    {
        Name = "MCP工具列表后调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "列出可用的MCP工具",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "MCP工具列表：mcp_list_clients、mcp_resource。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "mcp_list_clients",
                            Arguments = "{}"
                        }
                    ],
                    FollowUpText = "MCP工具列表：mcp_list_clients、mcp_resource。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_list_clients", Description = "应包含mcp_list_clients工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript McpClientCall => new()
    {
        Name = "MCP客户端调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看MCP客户端状态",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "MCP客户端状态已获取。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "mcp_client",
                            Arguments = "{\"action\":\"status\"}"
                        }
                    ],
                    FollowUpText = "MCP客户端状态已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_client", Description = "应包含mcp_client工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript McpToolCallThenFollowUp => new()
    {
        Name = "MCP工具调用后追问",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看MCP客户端列表",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "当前有2个MCP客户端连接。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "mcp_list_clients",
                            Arguments = "{}"
                        }
                    ],
                    FollowUpText = "当前有2个MCP客户端连接。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "mcp_list_clients", Description = "应包含mcp_list_clients" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "详细说说",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "两个客户端分别是：1) 文件系统MCP服务器 2) 数据库MCP服务器。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "追问应有回复" },
                ]
            }
        ]
    };
}

public static class ConcurrentRequestScripts
{
    public static ConversationScript RapidSequentialRequests => new()
    {
        Name = "快速连续请求",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "第一个问题",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "第一个问题的回答。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "第二个问题",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "第二个问题的回答。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "第三个问题",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "第三个问题的回答。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮应有回复" },
                ]
            }
        ]
    };

    public static ConversationScript InterleavedToolCallsAndText => new()
    {
        Name = "交替工具调用和文本",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看目录",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "目录内容已列出。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"ls\"}"
                        }
                    ],
                    FollowUpText = "目录内容已列出。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "第1轮应包含Bash" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "谢谢",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "不客气！"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮纯文本应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "再查看一次",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "目录内容同上。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"ls\"}"
                        }
                    ],
                    FollowUpText = "目录内容同上。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "第3轮应包含Bash" },
                ]
            }
        ]
    };
}
