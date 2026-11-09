namespace Host.Tests.Engine;

/// <summary>
/// QueryEngine 结构化输出捕获单元测试 — TASK071-C6
/// 覆盖 BuildStructuredOutputRetryExhaustedErrorChunk 和 BuildCompletionChunk 的 structuredOutput 参数传递
/// </summary>
public sealed class QueryEngineStructuredOutputTests {
    #region BuildStructuredOutputRetryExhaustedErrorChunk

    [Fact]
    public void BuildStructuredOutputRetryExhaustedErrorChunk_ShouldReturnErrorChunkWithCorrectStopReason() {
        var maxRetries = 5;

        var chunk = Core.Query.QueryEngine.BuildStructuredOutputRetryExhaustedErrorChunk(maxRetries);

        chunk.Type.Should().Be(AgentStreamChunkType.Error);
        chunk.StopReason.Should().Be("error_max_structured_output_retries");
        chunk.Content.Should().Contain("5");
        chunk.Content.Should().Contain("Structured output validation failed");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void BuildStructuredOutputRetryExhaustedErrorChunk_VariousMaxRetries_ShouldIncludeRetryCount(int maxRetries) {
        var chunk = Core.Query.QueryEngine.BuildStructuredOutputRetryExhaustedErrorChunk(maxRetries);

        chunk.Content.Should().Contain(maxRetries.ToString());
        chunk.StopReason.Should().Be("error_max_structured_output_retries");
    }

    [Fact]
    public void BuildStructuredOutputRetryExhaustedErrorChunk_ShouldNotHaveStructuredOutput() {
        var chunk = Core.Query.QueryEngine.BuildStructuredOutputRetryExhaustedErrorChunk(5);

        chunk.StructuredOutput.Should().BeNull();
    }

    #endregion

    #region BuildCompletionChunk - structuredOutput parameter

    [Fact]
    public void BuildCompletionChunk_WithStructuredOutput_ShouldCarryStructuredOutput() {
        var structuredOutput = JsonDocument.Parse("""{"name": "Alice", "age": 30}""").RootElement.Clone();

        var chunk = Core.Query.QueryEngine.BuildCompletionChunk(
            content: "done",
            elapsedMs: 1000,
            totalToolCalls: 2,
            totalTurns: 3,
            costUsd: 0.05m,
            cacheSafeParams: null,
            usage: null,
            stopReason: "end_turn",
            modelId: "test-model",
            structuredOutput: structuredOutput);

        chunk.Type.Should().Be(AgentStreamChunkType.Complete);
        chunk.StructuredOutput.Should().NotBeNull();
        var so = chunk.StructuredOutput;
        if (so is null) return;
        so.Value.ValueKind.Should().Be(JsonValueKind.Object);
        so.Value.GetProperty("name").GetString().Should().Be("Alice");
        so.Value.GetProperty("age").GetInt32().Should().Be(30);
    }

    [Fact]
    public void BuildCompletionChunk_WithoutStructuredOutput_ShouldHaveNullStructuredOutput() {
        var chunk = Core.Query.QueryEngine.BuildCompletionChunk(
            content: "done",
            elapsedMs: 500,
            totalToolCalls: 0,
            totalTurns: 1,
            costUsd: 0m,
            cacheSafeParams: null);

        chunk.Type.Should().Be(AgentStreamChunkType.Complete);
        chunk.StructuredOutput.Should().BeNull();
    }

    [Fact]
    public void BuildCompletionChunk_WithNullStructuredOutput_ShouldHaveNullStructuredOutput() {
        var chunk = Core.Query.QueryEngine.BuildCompletionChunk(
            content: "done",
            elapsedMs: 500,
            totalToolCalls: 0,
            totalTurns: 1,
            costUsd: 0m,
            cacheSafeParams: null,
            usage: null,
            stopReason: null,
            modelId: null,
            structuredOutput: null);

        chunk.StructuredOutput.Should().BeNull();
    }

    [Fact]
    public void BuildCompletionChunk_WithStructuredOutput_ShouldPreserveOtherFields() {
        var structuredOutput = JsonDocument.Parse("""{"result": "success"}""").RootElement.Clone();
        var usage = new TokenUsage {
            PromptTokens = 100,
            CompletionTokens = 50,
        };

        var chunk = Core.Query.QueryEngine.BuildCompletionChunk(
            content: "completed output",
            elapsedMs: 2500,
            totalToolCalls: 3,
            totalTurns: 4,
            costUsd: 0.12m,
            cacheSafeParams: null,
            usage: usage,
            stopReason: "end_turn",
            modelId: "gpt-4o",
            structuredOutput: structuredOutput);

        chunk.Content.Should().Be("completed output");
        chunk.ExecutionTimeMs.Should().Be(2500);
        chunk.TotalToolCalls.Should().Be(3);
        chunk.TotalTurns.Should().Be(4);
        chunk.CostUsd.Should().Be(0.12m);
        chunk.StopReason.Should().Be("end_turn");
        chunk.ModelId.Should().Be("gpt-4o");
        chunk.Usage.Should().NotBeNull();
        var chunkUsage = chunk.Usage;
        if (chunkUsage is null) return;
        chunkUsage.PromptTokens.Should().Be(100);
        chunkUsage.CompletionTokens.Should().Be(50);
        chunk.StructuredOutput.Should().NotBeNull();
        var so = chunk.StructuredOutput;
        if (so is null) return;
        so.Value.GetProperty("result").GetString().Should().Be("success");
    }

    [Fact]
    public void BuildCompletionChunk_DefaultStopReason_ShouldBeEndTurn() {
        var chunk = Core.Query.QueryEngine.BuildCompletionChunk(
            content: "done",
            elapsedMs: 100,
            totalToolCalls: 0,
            totalTurns: 1,
            costUsd: 0m,
            cacheSafeParams: null,
            stopReason: null);

        chunk.StopReason.Should().Be("end_turn");
    }

    [Fact]
    public void BuildCompletionChunk_ArrayStructuredOutput_ShouldCarryArrayData() {
        var structuredOutput = JsonDocument.Parse("[1, 2, 3]").RootElement.Clone();

        var chunk = Core.Query.QueryEngine.BuildCompletionChunk(
            content: "done",
            elapsedMs: 100,
            totalToolCalls: 1,
            totalTurns: 1,
            costUsd: 0m,
            cacheSafeParams: null,
            structuredOutput: structuredOutput);

        chunk.StructuredOutput.Should().NotBeNull();
        var so = chunk.StructuredOutput;
        if (so is null) return;
        so.Value.ValueKind.Should().Be(JsonValueKind.Array);
        so.Value.GetArrayLength().Should().Be(3);
    }

    #endregion
}
