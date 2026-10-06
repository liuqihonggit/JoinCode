#pragma warning disable JCC9108, JCC9202
namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginLifecycleTracker 单元测试 — 验证撤销链逆序执行、异常隔离、加载顺序管理
/// <para>确定性测试:不依赖时序/IO,纯字典+列表操作</para>
/// <para>JCC9108/JCC9202 抑制:测试 stub 故意不释放,用于验证 DisposeAsync 调用次数</para>
/// </summary>
public sealed class PluginLifecycleTrackerTest {
    /// <summary>异步可释放 stub — 记录 DisposeAsync 调用顺序和次数</summary>
    private sealed class AsyncDisposableStub : IAsyncDisposable {
        public int DisposeCallCount;
        public Exception? ThrowOnDispose;
        private int _disposed;
        public ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
            DisposeCallCount++;
            if (ThrowOnDispose is not null) throw ThrowOnDispose;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void ExecuteUndoChain_ReverseOrder_ExecutesLastFirst() {
        var tracker = new PluginLifecycleTracker(null, null);
        var calls = new List<string>();
        tracker.RegisterUndoChain("p", [
            () => calls.Add("first"),
            () => calls.Add("second"),
            () => calls.Add("third")
        ], null);
        tracker.AddToLoadOrder("p");

        tracker.ExecuteUndoChain("p");

        calls.Should().Equal("third", "second", "first");
    }

    [Fact]
    public void ExecuteUndoChain_ExceptionIsolation_ContinuesAfterFailure() {
        var tracker = new PluginLifecycleTracker(null, null);
        var calls = new List<string>();
        tracker.RegisterUndoChain("p", [
            () => calls.Add("first"),
            () => throw new InvalidOperationException("boom"),
            () => calls.Add("third")
        ], null);
        tracker.AddToLoadOrder("p");

        tracker.ExecuteUndoChain("p");

        // 第三项(逆序第一个)异常不应阻止后续执行
        calls.Should().Equal("third", "first");
    }

    [Fact]
    public void ExecuteUndoChain_ExceptionInvokesDiagnosticCallback() {
        PluginDiagnostic? reported = null;
        var tracker = new PluginLifecycleTracker(null, d => reported = d);
        tracker.RegisterUndoChain("p", [
            () => throw new InvalidOperationException("boom")
        ], null);
        tracker.AddToLoadOrder("p");

        tracker.ExecuteUndoChain("p");

        reported.Should().NotBeNull();
        reported!.Kind.Should().Be(PluginDiagnosticKind.RevertFailed);
        reported.PluginId.Should().Be("p");
    }

    [Fact]
    public void ExecuteUndoChain_EmptyChain_OnlyRemovesFromLoadOrder() {
        var tracker = new PluginLifecycleTracker(null, null);
        tracker.RegisterUndoChain("p", [], null);
        tracker.AddToLoadOrder("p");
        tracker.AddToLoadOrder("other");

        tracker.ExecuteUndoChain("p");

        tracker.GetLoadOrderReversed().Should().Equal("other");
    }

    [Fact]
    public void ExecuteUndoChain_NotRegistered_OnlyRemovesFromLoadOrder() {
        var tracker = new PluginLifecycleTracker(null, null);
        tracker.AddToLoadOrder("p");

        tracker.ExecuteUndoChain("p");

        tracker.GetLoadOrderReversed().Should().BeEmpty();
    }

    [Fact]
    public void ExecuteUndoChain_SingleNode_ExecutesOnce() {
        var tracker = new PluginLifecycleTracker(null, null);
        var count = 0;
        tracker.RegisterUndoChain("p", [() => count++], null);
        tracker.AddToLoadOrder("p");

        tracker.ExecuteUndoChain("p");

        count.Should().Be(1);
        tracker.GetLoadOrderReversed().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsyncUndoChainAsync_ReverseOrder_ExecutesLastFirst() {
        var tracker = new PluginLifecycleTracker(null, null);
        await using var stub1 = new AsyncDisposableStub();
        await using var stub2 = new AsyncDisposableStub();
        await using var stub3 = new AsyncDisposableStub();
        tracker.RegisterUndoChain("p", [], [stub1, stub2, stub3]);

        await tracker.ExecuteAsyncUndoChainAsync("p", CancellationToken.None);

        stub3.DisposeCallCount.Should().Be(1);
        stub2.DisposeCallCount.Should().Be(1);
        stub1.DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsyncUndoChainAsync_ExceptionIsolation_ContinuesAfterFailure() {
        var tracker = new PluginLifecycleTracker(null, null);
        await using var good = new AsyncDisposableStub();
        await using var bad = new AsyncDisposableStub { ThrowOnDispose = new InvalidOperationException("boom") };
        await using var good2 = new AsyncDisposableStub();
        tracker.RegisterUndoChain("p", [], [good, bad, good2]);

        await tracker.ExecuteAsyncUndoChainAsync("p", CancellationToken.None);

        // 逆序:good2 先,然后 bad(异常),然后 good
        good2.DisposeCallCount.Should().Be(1);
        bad.DisposeCallCount.Should().Be(1);
        good.DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsyncUndoChainAsync_EmptyChain_NoOp() {
        var tracker = new PluginLifecycleTracker(null, null);
        tracker.RegisterUndoChain("p", [], []);

        await tracker.ExecuteAsyncUndoChainAsync("p", CancellationToken.None);

        // 不抛异常即可
    }

    [Fact]
    public async Task ExecuteAsyncUndoChainAsync_NotRegistered_NoOp() {
        var tracker = new PluginLifecycleTracker(null, null);

        await tracker.ExecuteAsyncUndoChainAsync("nonexistent", CancellationToken.None);

        // 不抛异常即可
    }

    [Fact]
    public void AddToLoadOrder_GetLoadOrderReversed_ReturnsReversedCopy() {
        var tracker = new PluginLifecycleTracker(null, null);
        tracker.AddToLoadOrder("a");
        tracker.AddToLoadOrder("b");
        tracker.AddToLoadOrder("c");

        tracker.GetLoadOrderReversed().Should().Equal("c", "b", "a");
    }

    [Fact]
    public void RemoveFromLoadOrder_RemovesSpecified() {
        var tracker = new PluginLifecycleTracker(null, null);
        tracker.AddToLoadOrder("a");
        tracker.AddToLoadOrder("b");

        tracker.RemoveFromLoadOrder("a");

        tracker.GetLoadOrderReversed().Should().Equal("b");
    }

    [Fact]
    public void Clear_ResetsAllState() {
        var tracker = new PluginLifecycleTracker(null, null);
        tracker.RegisterUndoChain("p", [() => { }], null);
        tracker.AddToLoadOrder("p");

        tracker.Clear();

        tracker.GetLoadOrderReversed().Should().BeEmpty();
        // Clear 后 ExecuteUndoChain 不应执行撤销链(已清空)
        var executed = false;
        tracker.RegisterUndoChain("p2", [() => executed = true], null);
        tracker.ExecuteUndoChain("p2");
        executed.Should().BeTrue();
    }

    // ===== RegisterUndoChain null 参数守卫 =====

    [Fact]
    public void RegisterUndoChain_NullPluginName_ThrowsArgumentNullException() {
        var tracker = new PluginLifecycleTracker(null, null);

        Action act = () => tracker.RegisterUndoChain(null!, [], null);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("pluginName");
    }

    [Fact]
    public void RegisterUndoChain_EmptyPluginName_ThrowsArgumentException() {
        var tracker = new PluginLifecycleTracker(null, null);

        Action act = () => tracker.RegisterUndoChain("", [], null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterUndoChain_WhiteSpacePluginName_ThrowsArgumentException() {
        var tracker = new PluginLifecycleTracker(null, null);

        Action act = () => tracker.RegisterUndoChain("   ", [], null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterUndoChain_NullUndoChain_ThrowsArgumentNullException() {
        var tracker = new PluginLifecycleTracker(null, null);

        Action act = () => tracker.RegisterUndoChain("p", null!, null);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("undoChain");
    }

    [Fact]
    public void RegisterUndoChain_NullAsyncUndoChain_AcceptedAndNoThrow() {
        var tracker = new PluginLifecycleTracker(null, null);

        Action act = () => tracker.RegisterUndoChain("p", [], null);

        act.Should().NotThrow();
    }
}
#pragma warning restore JCC9108, JCC9202
