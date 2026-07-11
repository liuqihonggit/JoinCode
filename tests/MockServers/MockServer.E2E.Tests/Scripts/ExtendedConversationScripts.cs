namespace MockServer.E2E.Tests.Scripts;

public static class ToolCallScripts
{
    public static ConversationScript BashToolCall => new()
    {
        Name = "Bash工具调用对话",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看当前目录",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "当前工作目录为：/home/user/project",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"cd\"}"
                        }
                    ],
                    FollowUpText = "当前工作目录为：/home/user/project"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript ReadFileToolCall => new()
    {
        Name = "ReadFile工具调用对话",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "读取test.cs文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "文件内容如下：\nusing System;\n\nclass Program { static void Main() { } }",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Read",
                            Arguments = "{\"file_path\":\"/test/file.cs\"}"
                        }
                    ],
                    FollowUpText = "文件内容如下：\nusing System;\n\nclass Program { static void Main() { } }"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应包含ReadFile工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript MultiToolCalls => new()
    {
        Name = "多工具调用对话",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看目录并读取README",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "目录下有 README.md 文件，内容为：# JoinCode - AI工作流引擎",
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
                    FollowUpText = "目录下有 README.md 文件，内容为：# JoinCode - AI工作流引擎"
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

    public static ConversationScript UnknownToolCall => new()
    {
        Name = "未知工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "调用一个不存在的工具",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "该工具不存在，已返回错误。",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "NonExistentTool",
                            Arguments = "{\"param\":\"value\"}"
                        }
                    ],
                    FollowUpText = "该工具不存在，已返回错误。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "NonExistentTool", Description = "应包含工具调用" },
                    new OutputAssert { Type = AssertType.ToolCallFailed, Expected = "NonExistentTool", Description = "未知工具应失败" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript ThinkingThenResponse => new()
    {
        Name = "思考后回复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "思考一下再回答",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    ThinkingContent = "让我仔细想想这个问题...",
                    TextResponse = "经过思考，我认为答案是42。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript ToolCallWithFollowUpText => new()
    {
        Name = "工具调用后文本回复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看当前目录",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"cd\"}"
                        }
                    ],
                    FollowUpText = "当前工作目录为：/home/user/project"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };
}

public static class MultiTurnScripts
{
    public static ConversationScript FiveRoundMemory => new()
    {
        Name = "五轮对话记忆",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "请记住我的名字是张三",
                AiResponse = new MockResponseScript { TextResponse = "好的，我记住了，你叫张三。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我正在开发一个AI工作流项目",
                AiResponse = new MockResponseScript { TextResponse = "了解了，你在开发AI工作流项目。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我叫什么名字？",
                AiResponse = new MockResponseScript { TextResponse = "你叫张三。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我是谁？我在做什么？",
                AiResponse = new MockResponseScript { TextResponse = "你是张三，你正在开发一个AI工作流项目。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第4轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "/history",
                AiResponse = new MockResponseScript { TextResponse = "对话历史：\n[user]: 请记住我的名字是张三\n[assistant]: 好的，我记住了，你叫张三。\n[user]: 我正在开发一个AI工作流项目\n..." },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第5轮/history应有回复" }]
            }
        ]
    };

    public static ConversationScript ToolCallThenFollowUp => new()
    {
        Name = "工具调用后追问",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看当前目录",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "当前工作目录为：/home/user/project",
                    ToolCalls =
                    [
                        new MockToolCallScript { ToolName = "Bash", Arguments = "{\"command\":\"cd\"}" }
                    ],
                    FollowUpText = "当前工作目录为：/home/user/project"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "第1轮应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有助手回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "还有呢",
                AiResponse = new MockResponseScript { TextResponse = "目录下还有 src、tests、docs 等子目录。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮追问应有回复" }]
            }
        ]
    };
}

public static class PromptInjectionScripts
{
    public static ConversationScript NegativeKeyword => new()
    {
        Name = "负面关键词触发适应性提示词",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "this is horrible",
                AiResponse = new MockResponseScript { TextResponse = "抱歉让你感到不愉快，我会尽力改进。请告诉我具体哪里不满意？" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript KeepGoingKeyword => new()
    {
        Name = "继续关键词触发延续提示词",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "请列出3个C#最佳实践",
                AiResponse = new MockResponseScript { TextResponse = "1. 使用async/await进行异步编程\n2. 使用依赖注入\n3. 使用模式匹配" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "continue",
                AiResponse = new MockResponseScript { TextResponse = "4. 使用记录类型代替类\n5. 使用顶级语句\n6. 使用全局using" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮continue应有回复" }]
            }
        ]
    };

    public static ConversationScript NormalInputNoInjection => new()
    {
        Name = "正常输入不触发注入",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript { TextResponse = "你好！有什么可以帮你的？" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "[系统提示:", Description = "正常输入不应触发注入提示" },
                ]
            }
        ]
    };
}

public static class EdgeCaseScripts
{
    public static ConversationScript EmptyInput => new()
    {
        Name = "空输入处理",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript { TextResponse = "你好！" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" }]
            }
        ]
    };

    public static ConversationScript ExitCommand => new()
    {
        Name = "退出命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript { TextResponse = "你好！" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" }]
            }
        ]
    };

    public static ConversationScript TokenUsageNoError => new()
    {
        Name = "TokenUsage反序列化无错误",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "测试token使用量",
                AiResponse = new MockResponseScript { TextResponse = "Token使用量测试完成。" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "JsonException", Description = "不应有JSON反序列化异常" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "反序列化失败", Description = "不应有反序列化失败" },
                ]
            }
        ]
    };

    public static ConversationScript LongStreamingResponse => new()
    {
        Name = "长流式响应",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "讲个很长的故事",
                AiResponse = new MockResponseScript
                {
                    IsStreaming = true,
                    TextResponse = "从前有座山，山里有座庙，庙里有个老和尚在给小和尚讲故事。故事讲的是：很久很久以前，有一个美丽的王国，王国里住着一位智慧的国王。国王每天都会在花园里散步，思考如何让他的子民过上更好的生活。有一天，一位旅行者来到了王国，他带来了一个神奇的盒子..."
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有流式回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}

public static class PrefixCacheScripts
{
    public static ConversationScript ThreeTurnPrefixStable => new()
    {
        Name = "三轮对话前缀缓存稳定",
        Mode = ConversationMode.Interactive,
        DumpMessages = true,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript { TextResponse = "你好！有什么可以帮你的？" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "介绍一下你自己",
                AiResponse = new MockResponseScript { TextResponse = "我是AI助手，可以帮助你编写代码、分析问题。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "谢谢",
                AiResponse = new MockResponseScript { TextResponse = "不客气！还有其他问题吗？" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮应有回复" }]
            }
        ]
    };

    public static ConversationScript FiveTurnPrefixStable => new()
    {
        Name = "五轮对话前缀缓存稳定",
        Mode = ConversationMode.Interactive,
        DumpMessages = true,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "请记住我的名字是张三",
                AiResponse = new MockResponseScript { TextResponse = "好的，我记住了，你叫张三。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我正在开发一个AI工作流项目",
                AiResponse = new MockResponseScript { TextResponse = "了解了，你在开发AI工作流项目。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我叫什么名字？",
                AiResponse = new MockResponseScript { TextResponse = "你叫张三。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "我是谁？我在做什么？",
                AiResponse = new MockResponseScript { TextResponse = "你是张三，你正在开发一个AI工作流项目。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第4轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "总结一下我们的对话",
                AiResponse = new MockResponseScript { TextResponse = "你叫张三，正在开发AI工作流项目，我们进行了5轮对话。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第5轮应有回复" }]
            }
        ]
    };

    public static ConversationScript ToolCallPrefixStable => new()
    {
        Name = "工具调用后前缀缓存稳定",
        Mode = ConversationMode.Interactive,
        DumpMessages = true,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "查看当前目录",
                AiResponse = new MockResponseScript { TextResponse = "当前工作目录为：/home/user/project" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有助手回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "还有呢",
                AiResponse = new MockResponseScript { TextResponse = "目录下还有 src、tests、docs 等子目录。" },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮追问应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "读取README文件",
                AiResponse = new MockResponseScript { TextResponse = "README内容：# JoinCode - AI工作流引擎" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮应有助手回复" },
                ]
            }
        ]
    };
}

public static class ToolIterationScripts
{
    public static ConversationScript SequentialToolCalls => new()
    {
        Name = "连续工具调用迭代",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "先查看目录再读取文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript { ToolName = "Bash", Arguments = "{\"command\":\"ls\"}" }
                    ],
                    FollowUpText = "目录下有 config.json 文件。让我读取它。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "第1次应调用Bash" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "读取配置文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript { ToolName = "Read", Arguments = "{\"file_path\":\"config.json\"}" }
                    ],
                    FollowUpText = "配置文件内容：{\"version\": \"1.0\", \"debug\": true}"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "第2次应调用ReadFile" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有助手回复" },
                ]
            }
        ]
    };

    public static ConversationScript ToolCallThenErrorRecovery => new()
    {
        Name = "工具调用后错误恢复",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "读取一个不存在的文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript { ToolName = "Read", Arguments = "{\"file_path\":\"/nonexistent/file.txt\"}" }
                    ],
                    FollowUpText = "文件不存在，读取失败。让我尝试其他方式。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应调用ReadFile" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "错误后应有恢复回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "换一个存在的文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "好的，找到了存在的文件，内容如下：Hello World"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "恢复后应有正常回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "恢复后不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript ThreeRoundToolIteration => new()
    {
        Name = "三轮工具调用迭代",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我分析项目结构",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript { ToolName = "Bash", Arguments = "{\"command\":\"ls -la\"}" }
                    ],
                    FollowUpText = "项目根目录包含 src、tests、docs 三个目录。让我查看 src 目录。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "第1轮应调用Bash" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "继续",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript { ToolName = "Read", Arguments = "{\"file_path\":\"src/Program.cs\"}" }
                    ],
                    FollowUpText = "Program.cs 是入口文件，包含 Main 方法。让我看看配置文件。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "第2轮应调用ReadFile" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "总结一下",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "项目结构分析完成：1) 根目录有 src、tests、docs；2) 入口文件是 Program.cs；3) 配置使用 JSON 格式。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮总结应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "第3轮不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript ToolCallContextPreservation => new()
    {
        Name = "工具调用后上下文保持",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "我的名字是李四",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "好的，我记住了，你叫李四。"
                },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "查看当前目录",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript { ToolName = "Bash", Arguments = "{\"command\":\"pwd\"}" }
                    ],
                    FollowUpText = "当前目录是 /home/user/project"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "第2轮应调用Bash" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "我叫什么名字？",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你叫李四。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "工具调用后应保持上下文" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "李四", Description = "应记住之前的名字" },
                ]
            }
        ]
    };

    public static ConversationScript MixedToolAndTextConversation => new()
    {
        Name = "混合工具和文本对话",
        Mode = ConversationMode.Interactive,
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
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮纯文本应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "查看系统信息",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript { ToolName = "Bash", Arguments = "{\"command\":\"uname -a\"}" }
                    ],
                    FollowUpText = "系统信息：Linux x86_64, Kernel 6.1.0"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "第2轮应调用Bash" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮工具调用后应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "谢谢",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "不客气！还有其他问题吗？"
                },
                Asserts = [new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮纯文本应有回复" }]
            },
            new ConversationTurn
            {
                UserInput = "读取README",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript { ToolName = "Read", Arguments = "{\"file_path\":\"README.md\"}" }
                    ],
                    FollowUpText = "README内容：# My Project - A simple demo"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "第4轮应调用Read" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第4轮工具调用后应有回复" },
                ]
            }
        ]
    };
}
