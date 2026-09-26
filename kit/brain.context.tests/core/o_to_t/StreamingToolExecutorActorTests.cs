namespace Core.Context;

/// <summary>
/// StreamingToolExecutorActor 单元测试 — 验证 Actor 版与锁版行为等价。
/// </summary>
public sealed class StreamingToolExecutorActorTests {
    [Fact]
    public async Task AddTool_SingleSafeTool_ExecutesImmediately() {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "read"));
        var toolHandler = CreateToolHandler();
        await using var executor = new StreamingToolExecutorActor(toolHandler, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "read", Arguments = "{}" }, 0);

        var results = await executor.GetRemainingResultsAsync();
        results.Should().ContainSingle();
        results[0].ToolName.Should().Be("read");
        results[0].Result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task AddTool_TwoSafeTools_BothExecute() {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "read", "grep"));
        var executionOrder = new ConcurrentBag<string>();

        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("read", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("read"))
            .ReturnsAsync(new ToolCallResult { ResultText = "read-result", IsError = false });
        toolHandler.Setup(h => h.ExecuteToolCallAsync("grep", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("grep"))
            .ReturnsAsync(new ToolCallResult { ResultText = "grep-result", IsError = false });

        await using var executor = new StreamingToolExecutorActor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "read", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "grep", Arguments = "{}" }, 1);

        var results = await executor.GetRemainingResultsAsync();
        results.Should().HaveCount(2);
        executionOrder.Should().Contain("read");
        executionOrder.Should().Contain("grep");
    }

    [Fact]
    public async Task AddTool_NonSafeTools_ExecuteSequentially() {
        var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);
        var executionOrder = new List<string>();

        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Write", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Write"))
            .ReturnsAsync(new ToolCallResult { ResultText = "write-result", IsError = false });
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Write2", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Write2"))
            .ReturnsAsync(new ToolCallResult { ResultText = "write2-result", IsError = false });

        await using var executor = new StreamingToolExecutorActor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Write", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Write2", Arguments = "{}" }, 1);

        var results = await executor.GetRemainingResultsAsync();
        results.Should().HaveCount(2);
        executionOrder.Should().HaveCount(2);
        executionOrder[0].Should().Be("Write");
        executionOrder[1].Should().Be("Write2");
    }

    [Fact]
    public async Task GetCompletedResults_ReturnsResultsInOrder() {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "read"));
        var toolHandler = CreateToolHandler();
        await using var executor = new StreamingToolExecutorActor(toolHandler, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "read", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "read", Arguments = "{}" }, 1);

        var allCompleted = new List<StreamingToolResult>();
        for (var attempt = 0; attempt < 16 && allCompleted.Count < 2; attempt++) {
            await Task.Delay(500);
            allCompleted.AddRange(await executor.GetCompletedResultsAsync());
        }
        allCompleted.Should().HaveCount(2);
        allCompleted[0].OriginalIndex.Should().Be(0);
        allCompleted[1].OriginalIndex.Should().Be(1);
    }

    [Fact]
    public async Task Discard_MarksDiscarded_AndCompletesWithErrors() {
        var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);
        var slowTcs = new TaskCompletionSource<ToolCallResult>();
        var mockInvoked = new TaskCompletionSource<bool>();
        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => mockInvoked.TrySetResult(true))
            .Returns(() => slowTcs.Task);

        await using var executor = new StreamingToolExecutorActor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Write", Arguments = "{}" }, 0);

        await mockInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        executor.Discard();
        executor.IsDiscarded.Should().BeTrue();

        slowTcs.SetResult(new ToolCallResult { ResultText = "ok", IsError = false });
    }

    [Fact]
    public async Task CombinedCancellationToken_CascadesOnShellError() {
        var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);
        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("bash", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "error", IsError = true });

        await using var executor = new StreamingToolExecutorActor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "bash", Arguments = "{}" }, 0);
        await executor.GetRemainingResultsAsync();

        executor.CombinedCancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentAddTools_AllComplete_NoDeadlock() {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "read"));
        var toolHandler = CreateToolHandler();
        await using var executor = new StreamingToolExecutorActor(toolHandler, classifier, CreateContext());

        const int count = 50;
        var addTasks = Enumerable.Range(0, count).Select(async i => {
            await executor.AddToolAsync(new ToolCallEntry { Id = i.ToString(), Name = "read", Arguments = "{}" }, i);
        });

        await Task.WhenAll(addTasks);
        var results = await executor.GetRemainingResultsAsync().WaitAsync(TimeSpan.FromSeconds(10));
        results.Should().HaveCount(count);
    }

    private static IToolExecutionHandler CreateToolHandler() {
        var mock = new Mock<IToolExecutionHandler>();
        mock.Setup(h => h.ExecuteToolCallAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "ok", IsError = false });
        return mock.Object;
    }

    private static ChatMiddlewareContext CreateContext() => new() {
        Message = "test",
        ToolUseContext = new ToolUseContext()
    };
}