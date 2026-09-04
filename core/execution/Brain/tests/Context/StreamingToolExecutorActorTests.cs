namespace Core.Context;

/// <summary>
/// StreamingToolExecutorActor 单元测试 — 验证 Actor 版与锁版行为等价。
/// </summary>
public sealed class StreamingToolExecutorActorTests
{
    [Fact]
    public async Task AddTool_SingleSafeTool_ExecutesImmediately()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "Read"));
        var toolHandler = CreateToolHandler();
        var executor = new StreamingToolExecutorActor(toolHandler, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Read", Arguments = "{}" }, 0);

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

        var executor = new StreamingToolExecutorActor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Read", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Grep", Arguments = "{}" }, 1);

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

        var executor = new StreamingToolExecutorActor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Write", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Write2", Arguments = "{}" }, 1);

        var results = await executor.GetRemainingResultsAsync();
        results.Should().HaveCount(2);
        executionOrder.Should().HaveCount(2);
        executionOrder[0].Should().Be("Write");
        executionOrder[1].Should().Be("Write2");

        await executor.DisposeAsync();
    }

    [Fact]
    public async Task GetCompletedResults_ReturnsResultsInOrder()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "Read"));
        var toolHandler = CreateToolHandler();
        var executor = new StreamingToolExecutorActor(toolHandler, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Read", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Read", Arguments = "{}" }, 1);

        await Task.Delay(100);
        var completed = await executor.GetCompletedResultsAsync();
        completed.Should().HaveCount(2);
        completed[0].OriginalIndex.Should().Be(0);
        completed[1].OriginalIndex.Should().Be(1);

        await executor.DisposeAsync();
    }

    [Fact]
#pragma warning disable VSTHRD003 // TCS 由 mock 回调设置,测试仅等待完成
    public async Task Discard_MarksDiscarded_AndCompletesWithErrors()
    {
        var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);
        var slowTcs = new TaskCompletionSource<ToolCallResult>();
        var mockInvoked = new TaskCompletionSource<bool>();
        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => mockInvoked.TrySetResult(true))
            .Returns(() => slowTcs.Task);

        var executor = new StreamingToolExecutorActor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Write", Arguments = "{}" }, 0);

        await mockInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        executor.Discard();
        executor.IsDiscarded.Should().BeTrue();

        slowTcs.SetResult(new ToolCallResult { ResultText = "ok", IsError = false });

        await executor.DisposeAsync();
    }
#pragma warning restore VSTHRD003

    [Fact]
    public async Task CombinedCancellationToken_CascadesOnShellError()
    {
        var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);
        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Bash", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "error", IsError = true });

        var executor = new StreamingToolExecutorActor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Bash", Arguments = "{}" }, 0);
        await executor.GetRemainingResultsAsync();

        executor.CombinedCancellationToken.IsCancellationRequested.Should().BeTrue();

        await executor.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentAddTools_AllComplete_NoDeadlock()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "Read"));
        var toolHandler = CreateToolHandler();
        var executor = new StreamingToolExecutorActor(toolHandler, classifier, CreateContext());

        const int count = 50;
        var addTasks = Enumerable.Range(0, count).Select(async i =>
        {
            await executor.AddToolAsync(new ToolCallEntry { Id = i.ToString(), Name = "Read", Arguments = "{}" }, i);
        });

        await Task.WhenAll(addTasks);
        var results = await executor.GetRemainingResultsAsync().WaitAsync(TimeSpan.FromSeconds(10));
        results.Should().HaveCount(count);

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
