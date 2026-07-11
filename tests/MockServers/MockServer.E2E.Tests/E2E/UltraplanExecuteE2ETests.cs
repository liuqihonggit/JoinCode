namespace MockServer.E2E.Tests;

/// <summary>
/// /ultraplan --execute 自动执行模式 E2E 测试 — P0-A TDD 红阶段
/// 验证 --execute 标志应触发 PlanService.ExecutePlanWithResultAsync
/// 而非当前的"自动执行模式尚未实现"警告
/// </summary>
[Trait("Category", "Integration")]
public sealed class UltraplanExecuteE2ETests : CoverageTestBase
{
    public UltraplanExecuteE2ETests(ITestOutputHelper output) : base(output) { }

    /// <summary>
    /// 带 --execute 应真正调用 LLM 执行计划，不再输出"尚未实现"警告
    /// </summary>
    [Fact]
    public async Task Ultraplan_WithExecuteFlag_ShouldNotShowUnimplementedWarning()
    {
        var script = new ConversationScript
        {
            Name = "/ultraplan --execute 自动执行",
            Mode = ConversationMode.NonInteractive,
            Turns =
            [
                new ConversationTurn
                {
                    UserInput = "/ultraplan 测试目标 --execute",
                    AiResponse = new MockResponseScript
                    {
                        Type = MockResponseType.TextOnly,
                        TextResponse = "步骤1: 分析需求\n步骤2: 实施方案\n步骤3: 验证结果"
                    },
                    Asserts =
                    [
                        new OutputAssert
                        {
                            Type = AssertType.ContainsText,
                            Expected = "超级计划",
                            Description = "应进入超级计划模式"
                        },
                        new OutputAssert
                        {
                            Type = AssertType.NotContainsText,
                            Expected = "自动执行模式尚未实现",
                            Description = "不应再显示'未实现'警告 — P0-A 修复目标"
                        },
                    ]
                }
            ]
        };

        await RunScriptAsync(script).ConfigureAwait(true);
    }

    /// <summary>
    /// 不带 --execute 时应仅展示计划文本，行为保持现状（不进入执行模式）
    /// </summary>
    [Fact]
    public async Task Ultraplan_WithoutExecuteFlag_ShouldOnlyShowPlanText()
    {
        var script = new ConversationScript
        {
            Name = "/ultraplan 仅展示计划",
            Mode = ConversationMode.NonInteractive,
            Turns =
            [
                new ConversationTurn
                {
                    UserInput = "/ultraplan 测试目标",
                    AiResponse = new MockResponseScript
                    {
                        Type = MockResponseType.TextOnly,
                        TextResponse = "1. 分析需求\n2. 实施方案\n3. 验证结果"
                    },
                    Asserts =
                    [
                        new OutputAssert
                        {
                            Type = AssertType.ContainsText,
                            Expected = "超级计划",
                            Description = "应进入超级计划模式"
                        },
                        new OutputAssert
                        {
                            Type = AssertType.NotContainsText,
                            Expected = "自动执行模式尚未实现",
                            Description = "不带 --execute 也不应显示未实现警告（保持现状）"
                        },
                    ]
                }
            ]
        };

        await RunScriptAsync(script).ConfigureAwait(true);
    }

    /// <summary>
    /// 使用 -e 简写别名也应触发执行路径，不输出"未实现"警告
    /// </summary>
    [Fact]
    public async Task Ultraplan_WithExecuteAlias_ShouldNotShowUnimplementedWarning()
    {
        var script = new ConversationScript
        {
            Name = "/ultraplan -e 简写别名",
            Mode = ConversationMode.NonInteractive,
            Turns =
            [
                new ConversationTurn
                {
                    UserInput = "/ultraplan 简写测试 -e",
                    AiResponse = new MockResponseScript
                    {
                        Type = MockResponseType.TextOnly,
                        TextResponse = "简写别名执行结果"
                    },
                    Asserts =
                    [
                        new OutputAssert
                        {
                            Type = AssertType.ContainsText,
                            Expected = "超级计划",
                            Description = "应进入超级计划模式"
                        },
                        new OutputAssert
                        {
                            Type = AssertType.NotContainsText,
                            Expected = "自动执行模式尚未实现",
                            Description = "-e 别名同样应触发执行，不输出未实现警告"
                        },
                    ]
                }
            ]
        };

        await RunScriptAsync(script).ConfigureAwait(true);
    }
}
