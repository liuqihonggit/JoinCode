namespace MockServer.E2E.Tests.Core;

public sealed class ConversationOutputParserNoErrorsTests
{
    [Fact]
    public void NoErrors_WhenRawOutputContainsChineseErrorWordInToolList_ShouldNotTriggerFalsePositive()
    {
        var record = new ConversationTurnRecord
        {
            UserInput = "/tools",
            ToolCalls = [],
            AssistantResponse = "=== 可用工具 (292) ===\n  ⚙ ErrorRecovery\n     描述: 错误恢复工具",
            Errors = [],
            RawOutput = "=== 可用工具 (292) ===\n  ⚙ ErrorRecovery\n     描述: 错误恢复工具"
        };

        var assert = new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" };
        var results = ConversationOutputParser.EvaluateAsserts(record, [assert]);

        results[0].IsPassed.Should().BeTrue("工具列表中包含'错误'字样不应触发 NoErrors 误判");
    }

    [Fact]
    public void NoErrors_WhenActualErrorLine_ShouldFail()
    {
        var record = new ConversationTurnRecord
        {
            UserInput = "test",
            ToolCalls = [],
            AssistantResponse = "",
            Errors = ["错误: 连接超时"],
            RawOutput = "错误: 连接超时"
        };

        var assert = new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" };
        var results = ConversationOutputParser.EvaluateAsserts(record, [assert]);

        results[0].IsPassed.Should().BeFalse("实际错误行应导致 NoErrors 失败");
    }

    [Fact]
    public void NoErrors_WhenNoErrorsAndNoErrorKeywordInRawOutput_ShouldPass()
    {
        var record = new ConversationTurnRecord
        {
            UserInput = "/help",
            ToolCalls = [],
            AssistantResponse = "可用命令列表",
            Errors = [],
            RawOutput = "可用命令列表"
        };

        var assert = new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" };
        var results = ConversationOutputParser.EvaluateAsserts(record, [assert]);

        results[0].IsPassed.Should().BeTrue();
    }
}
