namespace Mcp.Tests;

/// <summary>
/// McpClient 响应处理丢弃异常回归测试
/// 根因: OnTransportMessageReceived 中 `ProcessResponseAsync(response).ConfigureAwait(false);`
/// 裸语句丢弃 Task，客户端释放后达到的响应会在 _requestLock.WaitAsync 抛 ObjectDisposedException，
/// 异常成为未观察异常被静默丢弃（多级报错缺失）。
/// </summary>
public sealed class McpClientResponseDiscardTests
{
    private sealed class TestClient : McpClientBase
    {
        public TestClient() : base(new McpClientOptions(), null) { }

        public override Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task DisconnectAsync(CancellationToken cancellationToken = default) { _requestLock.Dispose(); return Task.CompletedTask; }
        protected override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken) => Task.FromResult(new JsonRpcResponse { Id = request.Id });
        protected override Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>暴露释放后的锁，复现 ObjectDisposedException</summary>
        public void DisposeLockForTest() => _requestLock.Dispose();

        /// <summary>暴露 ProcessResponseAsync（当前调用方以裸语句丢弃其 Task）</summary>
        public Task ProcessResponseForTest(JsonRpcResponse response) => ProcessResponseAsync(response);

        /// <summary>暴露安全的 fire-and-forget 包裹器</summary>
        public Task FireAndForgetProcessResponseForTest(JsonRpcResponse response) => FireAndForgetProcessResponseAsync(response);

        /// <summary>注册 pending request 并返回其 TCS,模拟 SendRequestAsync 注册等待</summary>
        public TaskCompletionSource<JsonRpcResponse> RegisterPendingForTest(int requestId)
        {
            var tcs = new TaskCompletionSource<JsonRpcResponse>();
            _pendingRequests[requestId] = tcs;
            return tcs;
        }

        /// <summary>模拟等待者续体获取同锁(异常路径 catch 块行为)</summary>
#pragma warning disable VSTHRD003 // TCS 由 ProcessResponseAsync 设置,此处仅等待完成
        public async Task SimulateWaiterThenLockAsync(TaskCompletionSource<JsonRpcResponse> tcs)
        {
            await tcs.Task;
            using var guard = await _requestLock.TryLockAsync()
                ?? throw new System.TimeoutException("等待者续体获取锁超时");
        }
#pragma warning restore VSTHRD003
    }

    private static JsonRpcResponse CreateResponse(long id) => new() { Id = JsonRpcId.FromNumber(id) };

    [Fact]
    public async Task ProcessResponseAsync_AfterLockDisposed_ThrowsObjectDisposedException()
    {
        var client = new TestClient();
        client.DisposeLockForTest();

        var act = async () => await client.ProcessResponseForTest(CreateResponse(1)).ConfigureAwait(true);
        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task FireAndForgetProcessResponseAsync_AfterLockDisposed_DoesNotThrow()
    {
        var client = new TestClient();
        client.DisposeLockForTest();

        var act = async () => await client.FireAndForgetProcessResponseForTest(CreateResponse(1)).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FireAndForgetProcessResponseAsync_ValidPending_CompletesNormally()
    {
        var client = new TestClient();
        var act = async () => await client.FireAndForgetProcessResponseForTest(CreateResponse(99)).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 复现 ProcessResponseAsync 锁内 TrySetResult 导致等待者续体同线程同步重入锁死锁。
    /// 根因：ProcessResponseAsync 在 _requestLock 锁内调用 tcs.TrySetResult，唤醒的等待者续体
    /// 若在同线程同步执行并尝试获取 _requestLock（如异常 catch 路径），锁未释放 → 自等自死锁。
    /// 修复：TrySetResult 移到 guard.Dispose() 之后。> ADR: 0060
    /// </summary>
    [Fact]
    public async Task ProcessResponseAsync_LockInnerTrySetResult_DoesNotDeadlock()
    {
        var client = new TestClient();
        var tcs = client.RegisterPendingForTest(1);

        var waiterTask = client.SimulateWaiterThenLockAsync(tcs);

        await Task.Delay(100);

        await client.ProcessResponseForTest(CreateResponse(1)).WaitAsync(TimeSpan.FromSeconds(5));

        await waiterTask.WaitAsync(TimeSpan.FromSeconds(5));
        waiterTask.IsCompletedSuccessfully.Should().BeTrue("等待者续体在 5s 内完成说明未死锁");
    }
}