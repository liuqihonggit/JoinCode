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

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

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

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Write", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Write2", Arguments = "{}" }, 1);

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
            .ReturnsAsync(new ToolCallResult { ResultText = "cancelled", IsError = true });

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Bash", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Read", Arguments = "{}" }, 1);

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

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Grep", Arguments = "{}" }, 2);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Read", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "3", Name = "Grep", Arguments = "{}" }, 1);

        var results = await executor.GetRemainingResultsAsync();
        results[0].OriginalIndex.Should().Be(0);
        results[1].OriginalIndex.Should().Be(1);
        results[2].OriginalIndex.Should().Be(2);

        await executor.DisposeAsync();
    }

    [Fact]
#pragma warning disable VSTHRD003 // TaskCompletionSource 任务由测试控制
    public async Task FindNextExecutable_SafeAfterNonSafe_ShouldNotBeStarved()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "Read", "Grep"));
        var executionOrder = new List<string>();
        var readTcs = new TaskCompletionSource<ToolCallResult>();

        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Read", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Read:start"))
            .Returns(() => readTcs.Task);
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Write", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Write"))
            .ReturnsAsync(new ToolCallResult { ResultText = "write-result", IsError = false });
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Grep", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Grep"))
            .ReturnsAsync(new ToolCallResult { ResultText = "grep-result", IsError = false });

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Read", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Write", Arguments = "{}" }, 1);
        await executor.AddToolAsync(new ToolCallEntry { Id = "3", Name = "Grep", Arguments = "{}" }, 2);

        await Task.Delay(100);
        executionOrder.Should().Contain("Read:start");
        executionOrder.Should().NotContain("Write", "Write should wait because Read (safe) is executing and Write is non-safe");
        executionOrder.Should().Contain("Grep", "Grep (safe) should execute concurrently with Read (safe), not be starved by Write (non-safe) ahead in queue");

        readTcs.SetResult(new ToolCallResult { ResultText = "read-result", IsError = false });

        var results = await executor.GetRemainingResultsAsync();
        results.Should().HaveCount(3);
        results.Should().Contain(r => r.ToolName == "Read");
        results.Should().Contain(r => r.ToolName == "Write");
        results.Should().Contain(r => r.ToolName == "Grep");

        await executor.DisposeAsync();
    }
#pragma warning restore VSTHRD003

    [Fact]
#pragma warning disable VSTHRD003 // TaskCompletionSource 任务由测试控制
    public async Task AddTool_PowershellError_ShouldCancelSiblingTools()
    {
        var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);
        var psTcs = new TaskCompletionSource<ToolCallResult>();

        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync(ShellToolNameConstants.Powershell, It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => psTcs.Task);
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Read", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (string? _, string? _, Dictionary<string, JsonElement>? _, ChatMiddlewareContext _, CancellationToken ct) =>
            {
                await Task.Delay(5000, ct);
                return new ToolCallResult { ResultText = "read-result", IsError = false };
            });

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = ShellToolNameConstants.Powershell, Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Read", Arguments = "{}" }, 1);

        await Task.Delay(100);

        psTcs.SetResult(new ToolCallResult { ResultText = "ps error!", IsError = true });

        var results = await executor.GetRemainingResultsAsync();
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.ToolName == ShellToolNameConstants.Powershell && r.Result.IsError);
        results.Should().Contain(r => r.ToolName == "Read" && r.Result.IsError, "PowerShell error should cascade cancel sibling tools like Bash does");

        await executor.DisposeAsync();
    }
#pragma warning restore VSTHRD003

    [Fact]
#pragma warning disable VSTHRD003 // TaskCompletionSource 任务由测试控制
    public async Task AddTool_PowershellScriptError_ShouldCancelSiblingTools()
    {
        var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);
        var psTcs = new TaskCompletionSource<ToolCallResult>();

        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync(ShellToolNameConstants.PowershellScript, It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => psTcs.Task);
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Read", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (string? _, string? _, Dictionary<string, JsonElement>? _, ChatMiddlewareContext _, CancellationToken ct) =>
            {
                await Task.Delay(5000, ct);
                return new ToolCallResult { ResultText = "read-result", IsError = false };
            });

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = ShellToolNameConstants.PowershellScript, Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Read", Arguments = "{}" }, 1);

        await Task.Delay(100);

        psTcs.SetResult(new ToolCallResult { ResultText = "ps1 error!", IsError = true });

        var results = await executor.GetRemainingResultsAsync();
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.ToolName == ShellToolNameConstants.PowershellScript && r.Result.IsError);
        results.Should().Contain(r => r.ToolName == "Read" && r.Result.IsError, "PowerShellScript error should cascade cancel sibling tools like Bash does");

        await executor.DisposeAsync();
    }
#pragma warning restore VSTHRD003

    [Fact]
    public async Task UserCancellationToken_Cancelled_ShouldCancelExecutingTools()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, "Read"));
        using var userCts = new CancellationTokenSource();
        var toolStartedTcs = new TaskCompletionSource<bool>();

        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Read", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => toolStartedTcs.SetResult(true))
            .Returns(async (string? _, string? _, Dictionary<string, JsonElement>? _, ChatMiddlewareContext _, CancellationToken ct) =>
            {
                await Task.Delay(5000, ct);
                return new ToolCallResult { ResultText = "read-result", IsError = false };
            });

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext(), userCancellationToken: userCts.Token);

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Read", Arguments = "{}" }, 0);

        await toolStartedTcs.Task;
        userCts.Cancel();

        var results = await executor.GetRemainingResultsAsync();
        results.Should().ContainSingle();
        results[0].Result.IsError.Should().BeTrue("user cancellation should cause tool to be cancelled");
        results[0].Result.ResultText.Should().Contain("cancelled");

        await executor.DisposeAsync();
    }

    /// <summary>
    /// 回归测试：级联取消后队列调度不能卡死
    /// 根因：ProcessQueueAsync 循环条件误用 _combinedCt，级联取消后循环退出，
    /// 队列中等待的工具永远不会被执行，CompletionSource 永远不会 SetResult，
    /// 导致 GetRemainingResultsAsync 的 Task.WhenAll 永远等待
    /// </summary>
    [Fact]
    public async Task CascadeCancel_ShouldNotDeadlock_QueuedToolsMustStillComplete()
    {
        var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);
        var toolHandler = new Mock<IToolExecutionHandler>();
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Bash", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "error!", IsError = true });
        toolHandler.Setup(h => h.ExecuteToolCallAsync("Read", It.IsAny<string?>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<ChatMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "ok", IsError = false });

        var executor = new StreamingToolExecutor(toolHandler.Object, classifier, CreateContext());

        await executor.AddToolAsync(new ToolCallEntry { Id = "1", Name = "Bash", Arguments = "{}" }, 0);
        await executor.AddToolAsync(new ToolCallEntry { Id = "2", Name = "Read", Arguments = "{}" }, 1);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var results = await executor.GetRemainingResultsAsync().WaitAsync(timeoutCts.Token);

        results.Should().HaveCount(2, "both tools must complete even after cascade cancel");
        results.Should().Contain(r => r.ToolName == "Bash" && r.Result.IsError);
        results.Should().Contain(r => r.ToolName == "Read");

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
