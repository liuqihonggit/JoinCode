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

    [Fact]
    public void NoErrors_WhenDotNetILoggerErrorLine_ShouldNotTriggerFalsePositive()
    {
        var output = "[Tool] search_code(query=class)\n[FAIL] search_code\n  Tool 'search_code' execution failed\nwarn: McpToolRegistry.PermissionAwareToolExecutor[0]\n      => SpanId:abc123\nerror: Core.Context.ChatToolOrchestrator[0]\n      Tool pipeline error";
        var record = ConversationOutputParser.Parse(output);

        record.Errors.Should().BeEmpty(".NET ILogger 格式的 error:/warn: 日志行不应被误识别为错误");
    }

    [Fact]
    public void NoErrors_WhenRealErrorLine_ShouldBeDetected()
    {
        var output = "Error: something went wrong\nException: NullReferenceException";
        var record = ConversationOutputParser.Parse(output);

        record.Errors.Should().HaveCount(2, "真正的 Error: 和 Exception 行应被检测");
    }

    [Fact]
    public void Parse_ShouldCaptureToolResultLines()
    {
        var output = "[Tool] Read(file_path=test.txt)\n[FAIL] Read\n  File not found: test.txt\n  Current directory: /tmp";
        var record = ConversationOutputParser.Parse(output);

        record.ToolCalls.Should().HaveCount(1);
        record.ToolCalls[0].ToolName.Should().Be("Read");
        record.ToolCalls[0].IsSuccess.Should().BeFalse();
        record.ToolCalls[0].Result.Should().Contain("File not found");
    }
}
