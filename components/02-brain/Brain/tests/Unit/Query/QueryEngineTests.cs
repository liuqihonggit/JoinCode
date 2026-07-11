
namespace Core.Tests.Query;

public class QueryEngineTests
{
    [Fact]
    public void QueryEngineConfig_DefaultValues_ShouldBeCorrect()
    {
        var options = new QueryEngineConfig();

        options.Temperature.Should().Be(0.7f);
        options.MaxTokens.Should().Be(4000);
        options.TopP.Should().Be(0.95f);
        options.MaxToolCallIterations.Should().Be(128);
        options.EnableThinkingMode.Should().BeFalse();
    }

    [Fact]
    public void QueryStreamChunk_CreateContentChunk_ShouldHaveCorrectType()
    {
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Content,
            Content = "Test content"
        };

        chunk.Type.Should().Be(AgentStreamChunkType.Content);
        chunk.Content.Should().Be("Test content");
    }

    [Fact]
    public void QueryStreamChunk_CreateToolCallChunk_ShouldHaveToolInfo()
    {
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.ToolCallStart,
            ToolName = "test_tool",
            ToolCallNumber = 1
        };

        chunk.Type.Should().Be(AgentStreamChunkType.ToolCallStart);
        chunk.ToolName.Should().Be("test_tool");
        chunk.ToolCallNumber.Should().Be(1);
    }

    [Fact]
    public void QueryStreamChunk_CreateCompleteChunk_ShouldHaveStats()
    {
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            Content = "Final response",
            ExecutionTimeMs = 1500,
            TotalToolCalls = 3
        };

        chunk.Type.Should().Be(AgentStreamChunkType.Complete);
        chunk.ExecutionTimeMs.Should().Be(1500);
        chunk.TotalToolCalls.Should().Be(3);
    }

    [Theory]
    [InlineData(AgentStreamChunkType.Content)]
    [InlineData(AgentStreamChunkType.ThinkingStart)]
    [InlineData(AgentStreamChunkType.Thinking)]
    [InlineData(AgentStreamChunkType.ThinkingEnd)]
    [InlineData(AgentStreamChunkType.ToolCallStart)]
    [InlineData(AgentStreamChunkType.ToolCallEnd)]
    [InlineData(AgentStreamChunkType.Complete)]
    [InlineData(AgentStreamChunkType.Error)]
    public void AgentStreamChunkType_AllTypes_ShouldBeDefined(AgentStreamChunkType type)
    {
        // 确保所有类型都能被正确解析
        Enum.IsDefined(typeof(AgentStreamChunkType), type).Should().BeTrue();
    }

    [Fact]
    public void QueryEngineConfig_RetrySettings_ShouldHaveDefaults()
    {
        var config = new QueryEngineConfig();

        config.Retry.Should().NotBeNull();
        config.Retry.MaxRetries.Should().Be(3);
        config.Retry.RetryDelayMs.Should().Be(1000);
        config.Retry.EnableExponentialBackoff.Should().BeTrue();
    }

    [Fact]
    public void QueryStreamChunk_CreateErrorChunk_ShouldHaveContent()
    {
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Error,
            Content = "An error occurred"
        };

        chunk.Type.Should().Be(AgentStreamChunkType.Error);
        chunk.Content.Should().Be("An error occurred");
    }

    [Fact]
    public void QueryStreamChunk_CreateThinkingChunk_ShouldHaveCorrectType()
    {
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Thinking,
            Content = "Thinking about the problem..."
        };

        chunk.Type.Should().Be(AgentStreamChunkType.Thinking);
    }

    [Fact]
    public void QueryStreamChunk_CreateToolCallEndChunk_ShouldHaveResult()
    {
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.ToolCallEnd,
            ToolName = FileToolName.FileRead.ToValue(),
            ToolCallNumber = 2,
            Content = "Tool execution completed"
        };

        chunk.Type.Should().Be(AgentStreamChunkType.ToolCallEnd);
        chunk.ToolName.Should().Be(FileToolName.FileRead.ToValue());
        chunk.ToolCallNumber.Should().Be(2);
    }

    [Fact]
    public void QueryStreamChunk_DefaultValues_ShouldBeZeroOrNull()
    {
        var chunk = new QueryStreamChunk();

        chunk.Type.Should().Be(default(AgentStreamChunkType));
        chunk.Content.Should().BeNull();
        chunk.ToolName.Should().BeNull();
        chunk.ToolCallNumber.Should().BeNull();
        chunk.ToolResult.Should().BeNull();
        chunk.ExecutionTimeMs.Should().BeNull();
        chunk.TotalToolCalls.Should().Be(0);
        chunk.CostUsd.Should().Be(0);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void QueryEngineConfig_Temperature_ShouldAcceptValidValues(float temperature)
    {
        var config = new QueryEngineConfig { Temperature = temperature };
        config.Temperature.Should().Be(temperature);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(8000)]
    public void QueryEngineConfig_MaxTokens_ShouldAcceptValidValues(int maxTokens)
    {
        var config = new QueryEngineConfig { MaxTokens = maxTokens };
        config.MaxTokens.Should().Be(maxTokens);
    }

    [Fact]
    public void QueryEngineConfig_EnableThinkingMode_ShouldBeConfigurable()
    {
        var config = new QueryEngineConfig { EnableThinkingMode = true };
        config.EnableThinkingMode.Should().BeTrue();

        config.EnableThinkingMode = false;
        config.EnableThinkingMode.Should().BeFalse();
    }

    [Fact]
    public void QueryStreamChunk_CostUsd_ShouldBeSettable()
    {
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            CostUsd = 0.05m
        };

        chunk.CostUsd.Should().Be(0.05m);
    }

    [Fact]
    public void QueryStreamChunk_ExecutionTimeMs_ShouldBeNullable()
    {
        var chunk = new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Content,
            ExecutionTimeMs = null
        };

        chunk.ExecutionTimeMs.Should().BeNull();
    }

    [Fact]
    public void QueryEngineConfig_MaxToolCallIterations_ShouldHaveReasonableDefault()
    {
        var config = new QueryEngineConfig();

        // 默认128次应该足够大多数场景
        config.MaxToolCallIterations.Should().BeGreaterThan(10);
        config.MaxToolCallIterations.Should().BeLessThan(1000);
    }
}
