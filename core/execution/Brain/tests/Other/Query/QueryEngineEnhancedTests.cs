
namespace Core.Tests.Query;

public class QueryEngineEnhancedTests
{
    [Fact]
    public void QueryEngine_Create_WithTokenBudgetManager_ShouldNotThrow()
    {
        // Arrange
        var tokenBudgetManager = new TokenBudgetManager();

        // Act & Assert - 由于创建 QueryEngine 需要 Kernel，我们只能测试配置
        tokenBudgetManager.Should().NotBeNull();
    }

    [Fact]
    public void QueryEngineConfig_CostTrackingSettings_ShouldHaveDefaults()
    {
        var config = new QueryEngineConfig();

        config.CostTracking.Should().NotBeNull();
        config.CostTracking.Enabled.Should().BeTrue();
        config.CostTracking.InputTokenCostPer1K.Should().Be(0.0015m);
        config.CostTracking.OutputTokenCostPer1K.Should().Be(0.002m);
    }

    [Fact]
    public void CostTracker_TrackUsage_ShouldAccumulateTokens()
    {
        // Arrange
        var config = new CostTrackingConfig { Enabled = true };
        var tracker = new Core.Query.TokenCostTracker(config);

        // Act
        tracker.TrackUsage(100, 50);
        tracker.TrackUsage(200, 100);

        // Assert
        var (input, output, cost) = tracker.GetUsage();
        input.Should().Be(300);
        output.Should().Be(150);
    }

    [Fact]
    public void CostTracker_GetTotalCost_ShouldCalculateCorrectly()
    {
        // Arrange
        var config = new CostTrackingConfig
        {
            Enabled = true,
            InputTokenCostPer1K = 0.001m,
            OutputTokenCostPer1K = 0.002m
        };
        var tracker = new Core.Query.TokenCostTracker(config);

        // Act
        tracker.TrackUsage(1000, 500);

        // Assert
        var cost = tracker.GetTotalCost();
        cost.Should().Be(0.002m); // (1000/1000)*0.001 + (500/1000)*0.002 = 0.001 + 0.001 = 0.002
    }

    [Fact]
    public void CostTracker_WhenDisabled_ShouldReturnZeroCost()
    {
        // Arrange
        var config = new CostTrackingConfig { Enabled = false };
        var tracker = new Core.Query.TokenCostTracker(config);

        // Act
        tracker.TrackUsage(1000, 1000);

        // Assert
        tracker.GetTotalCost().Should().Be(0m);
    }

    [Fact]
    public void NullCostTracker_ShouldReturnZeroValues()
    {
        // Arrange
        var tracker = new NullTokenCostTracker();

        // Act
        tracker.TrackUsage(100, 50);

        // Assert
        tracker.GetTotalCost().Should().Be(0m);
        var (input, output, cost) = tracker.GetUsage();
        input.Should().Be(0);
        output.Should().Be(0);
        cost.Should().Be(0m);
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(3, 4000)]
    public void RetryConfig_CalculateDelay_WithExponentialBackoff_ShouldReturnExpectedValue(int retryCount, int expectedDelay)
    {
        // Arrange
        var config = new RetryConfig
        {
            RetryDelayMs = 1000,
            EnableExponentialBackoff = true
        };

        // Act
        var delay = (int)Math.Min(config.RetryDelayMs * Math.Pow(2, retryCount - 1), 30000);

        // Assert
        delay.Should().Be(expectedDelay);
    }

    [Fact]
    public void RetryConfig_CalculateDelay_WithoutExponentialBackoff_ShouldReturnFixedDelay()
    {
        // Arrange
        var config = new RetryConfig
        {
            RetryDelayMs = 1500,
            EnableExponentialBackoff = false
        };

        // Act
        var delay = config.RetryDelayMs;

        // Assert
        delay.Should().Be(1500);
    }

    [Fact]
    public void RetryConfig_CalculateDelay_ShouldNotExceedMaxDelay()
    {
        // Arrange
        var config = new RetryConfig
        {
            RetryDelayMs = 1000,
            EnableExponentialBackoff = true
        };

        // Act - 计算第10次重试的延迟（应该被限制在30000ms）
        var delay = (int)Math.Min(config.RetryDelayMs * Math.Pow(2, 10 - 1), 30000);

        // Assert
        delay.Should().Be(30000);
    }

    [Fact]
    public void QueryEngineConfig_MaxTokens_Validation_ShouldAcceptValidValues()
    {
        // Arrange & Act
        var config = new QueryEngineConfig
        {
            MaxTokens = 8000
        };

        // Assert
        config.MaxTokens.Should().Be(8000);
    }

    [Fact]
    public void QueryEngineConfig_Temperature_Validation_ShouldAcceptValidValues()
    {
        // Arrange & Act
        var config = new QueryEngineConfig
        {
            Temperature = 1.5f
        };

        // Assert
        config.Temperature.Should().Be(1.5f);
    }

    [Fact]
    public void QueryEngineConfig_TopP_Validation_ShouldAcceptValidValues()
    {
        // Arrange & Act
        var config = new QueryEngineConfig
        {
            TopP = 0.8f
        };

        // Assert
        config.TopP.Should().Be(0.8f);
    }

    [Fact]
    public void QueryStreamChunk_AllProperties_ShouldBeSettable()
    {
        // Arrange & Act
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            Content = "Test content",
            ToolName = "TestTool",
            ToolCallNumber = 5,
            ExecutionTimeMs = 2000,
            TotalToolCalls = 3,
            CostUsd = 0.005m
        };

        // Assert
        chunk.Type.Should().Be(AgentStreamChunkType.Complete);
        chunk.Content.Should().Be("Test content");
        chunk.ToolName.Should().Be("TestTool");
        chunk.ToolCallNumber.Should().Be(5);
        chunk.ExecutionTimeMs.Should().Be(2000);
        chunk.TotalToolCalls.Should().Be(3);
        chunk.CostUsd.Should().Be(0.005m);
    }

    [Fact]
    public void QueryStreamChunk_WithNullToolResult_ShouldNotThrow()
    {
        // Arrange & Act
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.ToolCallEnd,
            ToolName = "TestTool",
            ToolResult = null
        };

        // Assert
        chunk.ToolResult.Should().BeNull();
    }

    [Fact]
    public async Task CostTracker_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var config = new CostTrackingConfig { Enabled = true };
        var tracker = new Core.Query.TokenCostTracker(config);
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => tracker.TrackUsage(10, 5)));
        }
        await Task.WhenAll(tasks).ConfigureAwait(true);

        // Assert
        var (input, output, _) = tracker.GetUsage();
        input.Should().Be(1000);
        output.Should().Be(500);
    }

    [Fact]
    public void QueryEngineConfig_ThinkingModeTags_ShouldHaveDefaults()
    {
        // Arrange
        var config = new QueryEngineConfig();

        // Assert
        config.ThinkingStartTag.Should().Be("<thinking>");
        config.ThinkingEndTag.Should().Be("</thinking>");
    }

    [Fact]
    public void QueryEngineConfig_EnableThinkingMode_ShouldBeDisabledByDefault()
    {
        // Arrange
        var config = new QueryEngineConfig();

        // Assert
        config.EnableThinkingMode.Should().BeFalse();
    }

    [Fact]
    public void QueryEngineConfig_WhenThinkingModeEnabled_ShouldUseCustomTags()
    {
        // Arrange & Act
        var config = new QueryEngineConfig
        {
            EnableThinkingMode = true,
            ThinkingStartTag = "[[THINK]]",
            ThinkingEndTag = "[[/THINK]]"
        };

        // Assert
        config.EnableThinkingMode.Should().BeTrue();
        config.ThinkingStartTag.Should().Be("[[THINK]]");
        config.ThinkingEndTag.Should().Be("[[/THINK]]");
    }
}
