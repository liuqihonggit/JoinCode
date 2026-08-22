namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// 模态不匹配拦截 E2E 测试脚本 — 验证完整链路：
/// 媒介意图检测 → 注入标准报错文本 → ModelSearch 查找模型 → Agent 子代理 → 结果返回
/// </summary>
public static class ModalityMismatchScripts
{
    /// <summary>
    /// 图片生成意图 — gpt-4o 不支持 GenerateImage
    /// MockServer 模拟 LLM 收到报错后调用 ModelSearch 查找支持图片生成的模型
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
                            ToolName = "ModelSearch",
                            Arguments = """{"query":"map[generateImage]"}"""
                        }
                    ],
                    FollowUpText = "已找到支持图片生成的模型，将创建子代理执行。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "ModelSearch", Description = "模态不匹配时应调用ModelSearch查找模型" },
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
                            ToolName = "ModelSearch",
                            Arguments = """{"query":"map[readVideo]"}"""
                        }
                    ],
                    FollowUpText = "已找到支持视频识别的模型，将创建子代理执行。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "ModelSearch", Description = "视频识别不匹配时应调用ModelSearch查找模型" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// 图片识别意图 — gpt-4o（纯文本配置）不支持 ReadImage
    /// MockServer 模拟 LLM 收到报错后调用 ModelSearch 查找支持图片识别的模型
    /// </summary>
    public static ConversationScript ImageRecognitionMismatch => new()
    {
        Name = "模态不匹配-图片识别意图",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我看这张图片里有什么内容",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "ModelSearch",
                            Arguments = """{"query":"map[readImage]"}"""
                        }
                    ],
                    FollowUpText = "已找到支持图片识别的模型，将创建子代理执行。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "ModelSearch", Description = "图片识别不匹配时应调用ModelSearch查找模型" },
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// Agent 子代理完整链路 — 单轮模拟：ModelSearch → Agent 工具
    /// MockServer 先返回 ModelSearch 工具调用，FollowUpText 返回 Agent 工具调用
    /// ExtraTextResponses 提供子代理的 LLM 调用和最终跟进
    /// </summary>
    public static ConversationScript ModalityMismatchWithAgentSpawn => new()
    {
        Name = "模态不匹配-完整ModelSearch到Agent子代理链路",
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
                            ToolName = "ModelSearch",
                            Arguments = """{"query":"map[generateImage]"}"""
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "ModelSearch", Description = "应调用ModelSearch查找模型" },
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
    /// 识图完整链路 — ModelSearch(map[readImage]) → Agent 子代理(识图模型) → 看图片内容
    /// 纯文本模型收到识图任务 → 预检不匹配 → LLM 调用 ModelSearch 找识图模型 → Agent 子代理执行识图
    /// </summary>
    public static ConversationScript ImageRecognitionWithAgentSpawn => new()
    {
        Name = "模态不匹配-识图完整ModelSearch到Agent子代理链路",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我看这张图片里有什么内容",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "ModelSearch",
                            Arguments = """{"query":"map[readImage]"}"""
                        }
                    ],
                    FollowUpText = null
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ToolCallSucceeded, Expected = "ModelSearch", Description = "识图不匹配时应调用ModelSearch查找识图模型" },
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
                            Arguments = """{"description":"识别图片内容","prompt":"看这张图片里有什么","model":"agnes-image-2.0-flash"}"""
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
                    TextResponse = "任务完成。图片识别子代理已返回结果：图片里是一只橘猫趴在窗台上晒太阳。"
                }
            }
        ],
        MockServerExtraTextResponses =
        [
            "图片里是一只橘猫趴在窗台上晒太阳，毛色温暖柔和。"
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
