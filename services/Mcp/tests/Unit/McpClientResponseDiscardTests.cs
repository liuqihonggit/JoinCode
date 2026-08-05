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
}