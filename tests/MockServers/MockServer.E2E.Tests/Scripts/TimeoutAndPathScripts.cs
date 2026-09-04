namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// 脚本超时关键字 + 路径乱码检测 E2E 脚本
/// </summary>
public static class TimeoutAndPathScripts
{
    /// <summary>
    /// sleep 关键字自动延长超时 — 验证含 sleep 的命令不被默认超时终止
    /// </summary>
    public static ConversationScript SleepKeywordAutoExtendsTimeout => new()
    {
        Name = "sleep关键字自动延长超时",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "执行睡眠2秒的脚本",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "脚本执行完成",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"sleep 2; echo done\"}",
                            ToolResult = "done"
                        }
                    ],
                    FollowUpText = "脚本执行完成，输出 done"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash工具调用" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "超时", Description = "sleep 2s不应超时" },
                ]
            }
        ]
    };

    /// <summary>
    /// 超时关键字冲突直接报错 — 用户传入 timeout 不足时返回 Error 给 AI
    /// </summary>
    public static ConversationScript TimeoutKeywordConflictReturnsError => new()
    {
        Name = "超时关键字冲突直接报错",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "用1秒超时执行sleep 5",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "超时参数与脚本内等待时间冲突",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Bash",
                            Arguments = "{\"command\":\"sleep 5\",\"timeout\":1000}"
                        }
                    ],
                    FollowUpText = "命令内含5秒等待，但传入超时1秒不足。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ToolCallFailed, Expected = "Bash", Description = "冲突应导致工具调用失败" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "等待", Description = "应报含等待时间信息" },
                ]
            }
        ]
    };

    /// <summary>
    /// 乱码路径直接报错不进 ask 面板 — ask 模式下含 U+FFFD 的路径应直接 Invalid
    /// </summary>
    public static ConversationScript GarbledPathDirectErrorNoAskPanel => new()
    {
        Name = "乱码路径直接报错不进ask面板",
        ExtraEnvVars = new Dictionary<string, string>
        {
            ["JCC_PERMISSION_MODE"] = "ask"
        },
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "读取这个乱码路径文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "路径含乱码字符，无法读取",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Read",
                            Arguments = "{\"file_path\":\"D:\\\\other\\\\bad\\uFFFDfile.txt\"}"
                        }
                    ],
                    FollowUpText = "路径含乱码字符，直接报错"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ToolCallFailed, Expected = "Read", Description = "乱码路径应直接失败" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "需要确认", Description = "不应触发ask面板" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "需要用户确认", Description = "不应等待用户确认" },
                ]
            }
        ]
    };

    /// <summary>
    /// 工作目录外不存在路径直接报错 — ask 模式下不存在的路径应直接 Invalid 而非 Ask
    /// </summary>
    public static ConversationScript NonExistentPathDirectErrorNoAskPanel => new()
    {
        Name = "工作目录外不存在路径直接报错",
        ExtraEnvVars = new Dictionary<string, string>
        {
            ["JCC_PERMISSION_MODE"] = "ask"
        },
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "读取不存在的文件",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "路径不存在，无法读取",
                    ToolCalls =
                    [
                        new MockToolCallScript
                        {
                            ToolName = "Read",
                            Arguments = "{\"file_path\":\"D:\\\\nonexistent\\\\missing_file.txt\"}"
                        }
                    ],
                    FollowUpText = "路径不存在，直接报错"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ToolCallFailed, Expected = "Read", Description = "不存在路径应直接失败" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "需要确认", Description = "不应触发ask面板" },
                ]
            }
        ]
    };
}
