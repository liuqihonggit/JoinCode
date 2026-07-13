namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// /falv 结构化推理 E2E 测试脚本
/// </summary>
public static class FalvConversationScripts
{
    /// <summary>
    /// /falv --status 查看空引擎状态
    /// </summary>
    public static ConversationScript FalvStatusEmpty => new()
    {
        Name = "/falv --status 空引擎",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/falv --status",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "推理引擎状态", Description = "应显示推理引擎状态" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "假定: 0", Description = "空引擎假定数应为0" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "预算状态", Description = "应显示预算状态" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /falv 添加假定 + 查看状态
    /// </summary>
    public static ConversationScript FalvAddAssumption => new()
    {
        Name = "/falv 添加假定",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/falv test-assumption-e2e",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "假定", Description = "应显示假定标记" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "test-assumption-e2e", Description = "应显示假定内容" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "/falv --status",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "假定: 1", Description = "假定数应为1" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "test-assumption-e2e", Description = "应列出假定内容" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /falv --judge 裁决 + 预算消耗
    /// </summary>
    public static ConversationScript FalvJudgeWithBudget => new()
    {
        Name = "/falv --judge 裁决+预算",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/falv judge-test-assumption",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts = [new OutputAssert { Type = AssertType.ContainsText, Expected = "假定", Description = "应添加假定" }]
            },
            new ConversationTurn
            {
                UserInput = "/falv --judge",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "裁决结果", Description = "应显示裁决结果" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "/falv --budget",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "预算状态", Description = "应显示预算状态" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "轮次:", Description = "应显示轮次" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "Token:", Description = "应显示Token" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /falv 轮次耗尽 + 续费继续
    /// </summary>
    public static ConversationScript FalvBudgetExhaustAndRefill => new()
    {
        Name = "/falv 轮次耗尽+续费",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/falv exhaust-test",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts = [new OutputAssert { Type = AssertType.ContainsText, Expected = "假定", Description = "应添加假定" }]
            },
            new ConversationTurn
            {
                UserInput = "/falv --judge",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts = [new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "第1轮裁决不应有错误" }]
            },
            new ConversationTurn
            {
                UserInput = "/falv --judge",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts = [new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "第2轮裁决不应有错误" }]
            },
            new ConversationTurn
            {
                UserInput = "/falv --judge",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts = [new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "第3轮裁决不应有错误" }]
            },
            new ConversationTurn
            {
                UserInput = "/falv --judge",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts = [new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "第4轮裁决不应有错误" }]
            },
            new ConversationTurn
            {
                UserInput = "/falv --judge",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "轮次预算耗尽", Description = "第5轮应触发轮次耗尽提示" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "/falv --continue", Description = "应提示续费命令" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "/falv --continue default",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "续费", Description = "应显示续费信息" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "裁决结果", Description = "续费后应继续推理" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "续费后不应有错误" },
                ]
            },
            new ConversationTurn
            {
                UserInput = "/falv --budget",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "预算状态", Description = "应显示预算状态" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /falv --evidence 查看证据链
    /// </summary>
    public static ConversationScript FalvEvidenceEmpty => new()
    {
        Name = "/falv --evidence 空证据链",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/falv --evidence",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "证据链", Description = "应显示证据链标题" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "暂无证据", Description = "空引擎应显示暂无证据" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /falv --help 帮助信息
    /// </summary>
    public static ConversationScript FalvHelp => new()
    {
        Name = "/falv --help 帮助",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/falv --help",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "结构化推理引擎", Description = "应显示引擎描述" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "--status", Description = "应列出--status" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "--judge", Description = "应列出--judge" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "--continue", Description = "应列出--continue" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "--budget", Description = "应列出--budget" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "--cone", Description = "应列出--cone" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "--conflict", Description = "应列出--conflict" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "杀人罪", Description = "应列出证明标准预设" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /falv --cone 查看有限视锥
    /// </summary>
    public static ConversationScript FalvCone => new()
    {
        Name = "/falv --cone 有限视锥",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/falv cone-test-assumption",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts = [new OutputAssert { Type = AssertType.ContainsText, Expected = "假定", Description = "应添加假定" }]
            },
            new ConversationTurn
            {
                UserInput = "/falv --cone",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "有限视锥", Description = "应显示视锥标题" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /falv --conflict 检测视锥冲突
    /// </summary>
    public static ConversationScript FalvConflict => new()
    {
        Name = "/falv --conflict 视锥冲突",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/falv conflict-test-assumption",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts = [new OutputAssert { Type = AssertType.ContainsText, Expected = "假定", Description = "应添加假定" }]
            },
            new ConversationTurn
            {
                UserInput = "/falv --conflict",
                AiResponse = new MockResponseScript { Type = MockResponseType.TextOnly, TextResponse = "" },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "视锥冲突检测", Description = "应显示冲突检测标题" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}
