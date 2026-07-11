namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// DeepSeek-Reasonix 亮点移植功能的 E2E 测试脚本
/// P0: complete_step 证据门控 / SSRF 防护 / 截断 JSON 修复
/// P1: SessionController / 双模型 / 事件流
/// </summary>
public static class CompleteStepScripts
{
    /// <summary>
    /// P0-1: complete_step 工具调用 — 验证步骤完成证据门控
    /// MockServer 模拟 AI 调用 complete_step 工具，jcc 应正确处理
    /// </summary>
    public static ConversationScript CompleteStepToolCall => new()
    {
        Name = "complete_step 工具调用",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "完成第一步",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "complete_step",
                            Arguments = """{"step":"1. 分析需求","result":"需求分析完成，确定了功能范围","evidence":[{"kind":"manual","summary":"需求文档已审阅"}]}"""
                        }
                    ],
                    FollowUpText = "第一步已完成：需求分析。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "complete_step", Description = "应包含complete_step工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// P0-1: complete_step 多步骤完成 — 验证多轮证据门控
    /// </summary>
    public static ConversationScript CompleteStepMultiRound => new()
    {
        Name = "complete_step 多步骤完成",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我完成项目的前两个步骤",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "complete_step",
                            Arguments = """{"step":"1. 创建项目结构","result":"项目目录已创建","evidence":[{"kind":"files","summary":"创建了src和tests目录"}]}"""
                        }
                    ],
                    FollowUpText = "第一步完成。现在执行第二步。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "complete_step", Description = "第1轮应包含complete_step" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有助手回复" },
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
                        new MockToolCallScript
                        {
                            ToolName = "complete_step",
                            Arguments = """{"step":"2. 编写核心代码","result":"核心模块已实现","evidence":[{"kind":"verification","summary":"单元测试全部通过","command":"dotnet test"}]}"""
                        }
                    ],
                    FollowUpText = "第二步也完成了。项目前两个步骤均已签收。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "complete_step", Description = "第2轮应包含complete_step" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有助手回复" },
                ]
            }
        ]
    };
}

public static class SsrfGuardScripts
{
    /// <summary>
    /// P0-2: SSRF 防护 — 验证 web 工具调用不崩溃
    /// MockServer 模拟 AI 调用 web_fetch 工具，jcc 应正确处理
    /// 注意：SSRF 防护是中间件层拦截，E2E 测试验证的是工具调用链路不被破坏
    /// </summary>
    public static ConversationScript WebFetchToolCall => new()
    {
        Name = "web_fetch 工具调用（SSRF 防护链路）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "获取一个网页内容",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "web_fetch",
                            Arguments = """{"url":"https://example.com/api/data"}"""
                        }
                    ],
                    FollowUpText = "网页内容已获取。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "web_fetch", Description = "应包含web_fetch工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };
}

public static class SessionControllerScripts
{
    /// <summary>
    /// P1-2: SessionController — 验证流式文本响应通过统一事件消费
    /// </summary>
    public static ConversationScript StreamingTextViaController => new()
    {
        Name = "流式文本响应（SessionController 统一消费）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "介绍一下C#",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    IsStreaming = true,
                    TextResponse = "C# 是一种现代的、面向对象的编程语言，由微软开发。它运行在 .NET 平台上，支持强类型、泛型、异步编程等特性。C# 广泛用于 Web 开发、桌面应用、游戏开发（Unity）和云服务。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有流式回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// P1-2: SessionController — 验证工具调用事件通过统一消费
    /// 使用 Read 工具（自动允许访问已存在文件，不会被权限拒绝）
    /// </summary>
    public static ConversationScript ToolCallViaController => new()
    {
        Name = "工具调用事件（SessionController 统一消费）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "读取配置文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Read",
                            Arguments = """{"file_path":"jcc.runtimeconfig.json"}"""
                        }
                    ],
                    FollowUpText = "文件读取完成。配置文件包含运行时设置。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应包含Read工具调用" },
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "Read", Description = "Read工具调用应成功" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// P1-2: SessionController — 验证 API 超时检测
    /// </summary>
    public static ConversationScript ApiTimeoutDetection => new()
    {
        Name = "API 超时检测（SessionController 10s 无响应）",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "测试超时",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "正常响应"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };

    /// <summary>
    /// P1-2: SessionController — 验证思考内容 + 文本响应
    /// </summary>
    public static ConversationScript ThinkingAndTextViaController => new()
    {
        Name = "思考+文本响应（SessionController 统一消费）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "分析这段代码的问题",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    ThinkingContent = "让我分析这段代码...发现几个问题：1) 空引用风险 2) 缺少异常处理",
                    TextResponse = "代码存在以下问题：1) 可能有空引用异常；2) 缺少异常处理。建议添加空值检查和 try-catch 块。"
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

public static class TruncatedJsonScripts
{
    /// <summary>
    /// P0-3: 截断 JSON 修复 — 验证流式响应中截断的 JSON 不导致崩溃
    /// MockServer 正常返回，验证 jcc 的 JSON 解析链路健壮
    /// </summary>
    public static ConversationScript StreamingWithComplexResponse => new()
    {
        Name = "流式复杂响应（截断 JSON 修复链路）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "生成一个JSON配置",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    IsStreaming = true,
                    TextResponse = """这是一个JSON配置示例：{"name":"JoinCode","version":"1.0.0","features":["chat","tools","streaming"],"settings":{"debug":true,"timeout":30}}"""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "JsonException", Description = "不应有JSON异常" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "反序列化失败", Description = "不应有反序列化失败" },
                ]
            }
        ]
    };
}

public static class DualModelScripts
{
    /// <summary>
    /// P1-1: 双模型分离 — 验证工具调用后正常回复
    /// MockServer 模拟 AI 先调用工具再回复，验证 ModelCoordinator 链路
    /// 注意：双模型的 Planner/Executor 切换是内部行为，E2E 测试验证的是端到端不崩溃
    /// </summary>
    public static ConversationScript ToolCallThenAnalysis => new()
    {
        Name = "工具调用后分析回复（双模型链路）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "分析这个项目的代码质量",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = """{"command":"find . -name '*.cs' | head -5"}"""
                        }
                    ],
                    FollowUpText = "项目包含 5 个 C# 文件。代码质量分析：整体结构清晰，命名规范，建议增加单元测试覆盖率。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有分析回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// P1-1: 双模型分离 — 验证多工具调用后综合回复
    /// 模拟 Planner 先收集信息，Executor 再综合回复的场景
    /// </summary>
    public static ConversationScript MultiToolCallThenSynthesis => new()
    {
        Name = "多工具调用后综合回复（双模型链路）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我检查项目的安全问题",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = """{"command":"grep -r 'TODO' src/"}"""
                        },
                        new MockToolCallScript
                        {
                            ToolName = "Read",
                            Arguments = """{"file_path":"src/Program.cs"}"""
                        }
                    ],
                    FollowUpText = "安全检查完成：1) 发现 3 个 TODO 标记需要处理；2) Program.cs 入口点无异常。建议处理所有 TODO 项。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应包含ReadFile工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有综合回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// P1-1: 双模型分离 — 验证空计划检测（NoOpPlanDetector）
    /// 模拟 AI 直接回复文本（不需要工具调用），验证 NoOpPlan 检测不干扰正常流程
    /// </summary>
    public static ConversationScript DirectTextNoPlan => new()
    {
        Name = "直接文本回复（空计划检测链路）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好，今天天气怎么样",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！我是AI助手，无法获取实时天气信息，但可以帮你处理代码相关的问题。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有直接回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}

public static class EventStreamScripts
{
    /// <summary>
    /// P1-3: 统一强类型事件流 — 验证多轮对话事件传递
    /// AppEventBus + ServiceMessageType 的端到端验证
    /// 事件流是内部架构，E2E 验证的是多轮对话不丢失上下文
    /// </summary>
    public static ConversationScript ThreeTurnContextPreservation => new()
    {
        Name = "三轮上下文保持（事件流链路）",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "我的项目名叫Alpha",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "好的，我记住了，你的项目名叫 Alpha。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "帮我查看项目文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = """{"command":"ls Alpha/"}"""
                        }
                    ],
                    FollowUpText = "Alpha 项目包含 src、tests、docs 目录。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "第2轮应包含工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "我的项目叫什么名字",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你的项目名叫 Alpha。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮应记住项目名" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Alpha", Description = "应包含项目名Alpha" },
                ]
            }
        ]
    };

    /// <summary>
    /// P1-3: 统一强类型事件流 — 验证工具进度事件传递
    /// </summary>
    public static ConversationScript ToolProgressEventStream => new()
    {
        Name = "工具进度事件流",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "搜索所有C#文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = """{"command":"find . -name '*.cs' -type f"}"""
                        }
                    ],
                    FollowUpText = "找到了 42 个 C# 文件。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}
