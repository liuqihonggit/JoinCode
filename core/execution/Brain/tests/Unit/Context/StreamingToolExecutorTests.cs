namespace Core.Context;

public sealed class StreamingToolExecutorTests
{
    [Fact]
    public async Task AddTool_SingleSafeTool_ExecutesImmediately()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "Read"));
        var toolHandler = CreateToolHandler();
        var executor = new StreamingToolExecutor(toolHandler, classifier, CreateContext());

        executor.AddTool(new ToolCallEntry { Id = "1", Name = "Read", Arguments = "{}" }, 0);

        var results = await executor.GetRemainingResultsAsync();
        results.Should().ContainSingle();
        results[0].ToolName.Should().Be("Read");
        results[0].Result.IsError.Should().BeFalse();

        await executor.DisposeAsync();
    }

    [Fact]
    public async Task AddTool_TwoSafeTools_BothExecute()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "Read", "Grep"));
        var executionOrder = new List<string>();

        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Read", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Read"))
            .ReturnsAsync(new ToolCallResult { ResultText = "read-result", IsError = false });
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Grep", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Grep"))
            .ReturnsAsync(new ToolCallResult { ResultText = "grep-result", IsError = false });

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

        executor.AddTool(new ToolCallEntry { Id = "1", Name = "Read", Arguments = "{}" }, 0);
        executor.AddTool(new ToolCallEntry { Id = "2", Name = "Grep", Arguments = "{}" }, 1);

        var results = await executor.GetRemainingResultsAsync();
        results.Should().HaveCount(2);
        executionOrder.Should().Contain("Read");
        executionOrder.Should().Contain("Grep");

        await executor.DisposeAsync();
    }

    [Fact]
    public async Task AddTool_NonSafeTools_ExecuteSequentially()
    {
        var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);
        var executionOrder = new List<string>();

        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Write", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Write"))
            .ReturnsAsync(new ToolCallResult { ResultText = "write-result", IsError = false });
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Write2", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Write2"))
            .ReturnsAsync(new ToolCallResult { ResultText = "write2-result", IsError = false });

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

        executor.AddTool(new ToolCallEntry { Id = "1", Name = "Write", Arguments = "{}" }, 0);
        executor.AddTool(new ToolCallEntry { Id = "2", Name = "Write2", Arguments = "{}" }, 1);

        var results = await executor.GetRemainingResultsAsync();
        results.Should().HaveCount(2);
        executionOrder[0].Should().Be("Write");
        executionOrder[1].Should().Be("Write2");

        await executor.DisposeAsync();
    }

    [Fact]
    public async Task AddTool_BashError_CancelsSiblingTools()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "Read"));
        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Bash", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "error!", IsError = true });
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Read", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

        executor.AddTool(new ToolCallEntry { Id = "1", Name = "Bash", Arguments = "{}" }, 0);
        executor.AddTool(new ToolCallEntry { Id = "2", Name = "Read", Arguments = "{}" }, 1);

        var results = await executor.GetRemainingResultsAsync();
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.ToolName == "Bash" && r.Result.IsError);
        results.Should().Contain(r => r.ToolName == "Read" && r.Result.IsError);

        await executor.DisposeAsync();
    }

    [Fact]
    public async Task GetCompletedResults_ReturnsInOriginalOrder()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "Read", "Grep"));
        var toolHandler = CreateToolHandler();
        var executor = new StreamingToolExecutor(toolHandler, classifier, CreateContext());

        executor.AddTool(new ToolCallEntry { Id = "1", Name = "Grep", Arguments = "{}" }, 2);
        executor.AddTool(new ToolCallEntry { Id = "2", Name = "Read", Arguments = "{}" }, 0);
        executor.AddTool(new ToolCallEntry { Id = "3", Name = "Grep", Arguments = "{}" }, 1);

        var results = await executor.GetRemainingResultsAsync();
        results[0].OriginalIndex.Should().Be(0);
        results[1].OriginalIndex.Should().Be(1);
        results[2].OriginalIndex.Should().Be(2);

        await executor.DisposeAsync();
    }

    private static IToolExecutionHandler CreateToolHandler()
    {
        var mock = new Mock<IToolExecutionHandler>();
        mock.Setup(h => h.ExecuteToolCallAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "ok", IsError = false });
        return mock.Object;
    }

    private static ChatMiddlewareContext CreateContext() => new()
    {
        Message = "test",
        ToolUseContext = new ToolUseContext()
    };
}
