namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// ToolHealthRecord.GenerateStatusDescription 单元测试 — 验证"状态+条件+目的"自然语言描述
/// </summary>
public sealed class ToolHealthRecordStatusDescriptionTest {
    private static ToolHealthRecord CreateRecord(
        int success = 0, int fail = 0, int score = 0,
        int consecutiveFailures = 0, bool isEnabled = true,
        DateTime? lastAdjusted = null) =>
        new() {
            ToolName = "test_tool",
            SuccessCount = success,
            FailCount = fail,
            Score = score,
            ConsecutiveFailures = consecutiveFailures,
            IsEnabled = isEnabled,
            LastAdjusted = lastAdjusted ?? DateTime.UtcNow
        };

    [Fact]
    public void GenerateStatusDescription_NoCalls_ReturnsUnusedLabel() {
        var record = CreateRecord();
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("未使用");
        desc.Should().Contain("共调用 0 次");
    }

    [Fact]
    public void GenerateStatusDescription_HighCallCount_ReturnsHotLabel() {
        var record = CreateRecord(success: 45, fail: 5, score: 80);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("热");
        desc.Should().Contain("共调用 50 次");
    }

    [Fact]
    public void GenerateStatusDescription_MediumCallCount_ReturnsWarmLabel() {
        var record = CreateRecord(success: 9, fail: 1, score: 10);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("温");
        desc.Should().Contain("共调用 10 次");
    }

    [Fact]
    public void GenerateStatusDescription_LowCallCount_ReturnsColdLabel() {
        var record = CreateRecord(success: 1, fail: 0, score: 1);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("冷");
        desc.Should().Contain("共调用 1 次");
    }

    [Fact]
    public void GenerateStatusDescription_HighSuccessRate_ReturnsHealthyLabel() {
        var record = CreateRecord(success: 18, fail: 2, score: 50);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("健康");
    }

    [Fact]
    public void GenerateStatusDescription_LowSuccessRate_ReturnsAbnormalLabel() {
        var record = CreateRecord(success: 2, fail: 8, score: -50);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("异常");
    }

    [Fact]
    public void GenerateStatusDescription_HighScore_ReturnsHighScoreLabel() {
        var record = CreateRecord(success: 60, fail: 0, score: 80);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("高评分");
    }

    [Fact]
    public void GenerateStatusDescription_DangerScore_ReturnsDangerLabel() {
        var record = CreateRecord(success: 0, fail: 10, score: -50);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("危险");
    }

    [Fact]
    public void GenerateStatusDescription_WithConsecutiveFailures_IncludesFailureCount() {
        var record = CreateRecord(success: 5, fail: 5, score: -20, consecutiveFailures: 3);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("连续失败 3 次");
    }

    [Fact]
    public void GenerateStatusDescription_IdleForHours_IncludesIdleTime() {
        var record = CreateRecord(success: 5, fail: 0, score: 5,
            lastAdjusted: DateTime.UtcNow.AddHours(-3));
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("空闲");
    }

    [Fact]
    public void GenerateStatusDescription_Disabled_IncludesCircuitBreaker() {
        var record = CreateRecord(success: 5, fail: 5, score: -30, isEnabled: false);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("已熔断");
    }

    [Fact]
    public void GenerateStatusDescription_WithChain_IncludesChainRecommendation() {
        var record = CreateRecord(success: 10, fail: 0, score: 20);
        var desc = record.GenerateStatusDescription(["tool_b", "tool_c"]);
        desc.Should().Contain("推荐链路 → tool_b → tool_c");
    }

    [Fact]
    public void GenerateStatusDescription_WithNullChain_OmitsChainSection() {
        var record = CreateRecord(success: 10, fail: 0, score: 20);
        var desc = record.GenerateStatusDescription(null);
        desc.Should().NotContain("推荐链路");
    }

    [Fact]
    public void GenerateStatusDescription_WithEmptyChain_OmitsChainSection() {
        var record = CreateRecord(success: 10, fail: 0, score: 20);
        var desc = record.GenerateStatusDescription([]);
        desc.Should().NotContain("推荐链路");
    }

    [Fact]
    public void GenerateStatusDescription_ContainsToolName() {
        var record = new ToolHealthRecord {
            ToolName = "my_custom_tool",
            SuccessCount = 1,
            FailCount = 0,
            Score = 1
        };
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("my_custom_tool");
    }

    [Fact]
    public void GenerateStatusDescription_MediumSuccessRate_ReturnsUnstableLabel() {
        var record = CreateRecord(success: 5, fail: 5, score: 0);
        var desc = record.GenerateStatusDescription();
        desc.Should().Contain("不稳定");
    }
}
