namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// 模态不匹配拦截 E2E 测试脚本 — 验证完整链路：
/// 媒介意图检测 → 注入系统提示 → AskUserQuestion → Agent 子代理 → 结果返回
/// </summary>
public static class ModalityMismatchScripts
{
    /// <summary>
    /// 图片生成意图 — gpt-4o 不支持 GenerateImage
    /// MockServer 模拟 LLM 收到注入提示后调用 AskUserQuestion
    /// </summary>
    public static ConversationScript ImageGenerationMismatch => new()
    {
        Name = "模态不匹配-图片生成意图",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我画一张日落的海滩图片",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "AskUserQuestion",
                            Arguments = """{"questions":"[{\"question\":\"当前模型 gpt-4o 不支持图片生成。如何处理？\",\"header\":\"模态处理\",\"options\":[{\"label\":\"自动委托\",\"description\":\"用支持图片生成的模型创建子代理\"},{\"label\":\"手工指定模型\",\"description\":\"从支持图片生成的模型列表中选择\"},{\"label\":\"不允许\",\"description\":\"取消操作\"},{\"label\":\"用户输入内容\",\"description\":\"自由输入文本说明\"}]}"}"""
                        }
                    ],
                    FollowUpText = "好的，我将用支持图片生成的模型创建子代理来处理。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "AskUserQuestion", Description = "模态不匹配时应调用AskUserQuestion" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// 视频识别意图 — gpt-4o 不支持 ReadVideo
    /// </summary>
    public static ConversationScript VideoRecognitionMismatch => new()
    {
        Name = "模态不匹配-视频识别意图",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我看这个视频里有什么内容",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "AskUserQuestion",
                            Arguments = """{"questions":"[{\"question\":\"当前模型 gpt-4o 不支持视频识别。如何处理？\",\"header\":\"模态处理\",\"options\":[{\"label\":\"自动委托\",\"description\":\"用支持视频识别的模型创建子代理\"},{\"label\":\"手工指定模型\",\"description\":\"从支持视频识别的模型列表中选择\"},{\"label\":\"不允许\",\"description\":\"取消操作\"},{\"label\":\"用户输入内容\",\"description\":\"自由输入文本说明\"}]}"}"""
                        }
                    ],
                    FollowUpText = "好的，我将用支持视频识别的模型创建子代理来处理。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "AskUserQuestion", Description = "视频识别不匹配时应调用AskUserQuestion" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// Agent 子代理完整链路 — 单轮模拟：AskUserQuestion → Agent 工具
    /// MockServer 先返回 AskUserQuestion 工具调用，FollowUpText 返回 Agent 工具调用
    /// ExtraTextResponses 提供子代理的 LLM 调用和最终跟进
    /// </summary>
    public static ConversationScript ModalityMismatchWithAgentSpawn => new()
    {
        Name = "模态不匹配-完整Agent子代理链路",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我画一张猫的图片",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "AskUserQuestion",
                            Arguments = """{"questions":"[{\"question\":\"当前模型 gpt-4o 不支持图片生成。如何处理？\",\"header\":\"模态处理\",\"options\":[{\"label\":\"自动委托\",\"description\":\"用支持图片生成的模型创建子代理\"},{\"label\":\"手工指定模型\",\"description\":\"从支持图片生成的模型列表中选择\"},{\"label\":\"不允许\",\"description\":\"取消操作\"},{\"label\":\"用户输入内容\",\"description\":\"自由输入文本说明\"}]}"}"""
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "AskUserQuestion", Description = "应调用AskUserQuestion询问用户" },
                ]
            }
        ],
        MockServerExtraTurns =
        [
            new ConversationTurn
            {
                UserInput = "",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Agent",
                            Arguments = """{"description":"生成猫的图片","prompt":"画一张可爱的猫咪图片","model":"dall-e-3"}"""
                        }
                    ],
                    FollowUpText = null
                }
            },
            new ConversationTurn
            {
                UserInput = "",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "任务完成。图片生成子代理已返回结果：一只可爱的橘猫趴在窗台上晒太阳。"
                }
            }
        ],
        MockServerExtraTextResponses =
        [
            "一只可爱的橘猫趴在窗台上晒太阳，毛色温暖柔和。"
        ]
    };

    /// <summary>
    /// 纯文本消息不应触发模态不匹配 — 验证无注入提示时正常对话
    /// </summary>
    public static ConversationScript NoMismatchForTextOnly => new()
    {
        Name = "纯文本消息-不触发模态不匹配",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我写一个快速排序算法",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "好的，这是一个快速排序算法的实现：\n\n```python\ndef quicksort(arr):\n    if len(arr) <= 1:\n        return arr\n    pivot = arr[len(arr) // 2]\n    left = [x for x in arr if x < pivot]\n    middle = [x for x in arr if x == pivot]\n    right = [x for x in arr if x > pivot]\n    return quicksort(left) + middle + quicksort(right)\n```"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有正常回复" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "模态不匹配", Description = "纯文本消息不应触发模态不匹配提示" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}
