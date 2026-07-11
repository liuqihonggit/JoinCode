namespace MockServer.E2E.Tests.Scripts;

public static class BasicConversationScripts
{
    public static ConversationScript SingleTurnTextOnly => new()
    {
        Name = "单轮纯文本对话",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "你好",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你好！我是AI助手，有什么可以帮你的？"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript SingleTurnNonInteractive => new()
    {
        Name = "非交互模式单轮对话",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "Hello",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Hello! I am an AI assistant. How can I help you?"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript SingleTurnWithToolCall => new()
    {
        Name = "单轮工具调用对话",
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
                            Arguments = "{\"command\":\"cd\"}",
                            ToolResult = "/home/user/project"
                        }
                    ],
                    FollowUpText = "当前工作目录为：/home/user/project"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有助手回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    public static ConversationScript MultiTurnMemory => new()
    {
        Name = "多轮对话记忆",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "请记住我的名字是张三",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "好的，我记住了，你叫张三。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第1轮应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "我叫什么名字？",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你叫张三。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第2轮应有回复" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "我是谁？",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "你是张三。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "第3轮应有回复" },
                ]
            }
        ]
    };

    public static ConversationScript StreamingResponse => new()
    {
        Name = "流式响应测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "讲个故事",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    IsStreaming = true,
                    TextResponse = "从前有座山，山里有座庙，庙里有个老和尚在给小和尚讲故事。故事讲的是：从前有座山..."
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有流式回复" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "JsonException", Description = "不应有JSON反序列化错误" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "反序列化失败", Description = "不应有反序列化失败" },
                ]
            }
        ]
    };
}
