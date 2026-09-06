namespace Mcp.Tests;

/// <summary>
/// McpClient 响应处理丢弃异常回归测试
/// 根因: OnTransportMessageReceived 中 `ProcessResponseAsync(response).ConfigureAwait(false);`
/// 裸语句丢弃 Task，客户端释放后到达的响应在 Actor 已释放时抛 ObjectDisposedException，
/// 异常成为未观察异常被静默丢弃（多级报错缺失）。
/// </summary>
public sealed class McpClientResponseDiscardTests
{
    private sealed class TestClient : McpClientBase
    {
        public TestClient() : base(new McpClientOptions(), null) { }

        public override Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        protected override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken) => Task.FromResult(new JsonRpcResponse { Id = request.Id });
        protected override Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
        public override async ValueTask DisposeAsync() => await _requestRegistry.DisposeAsync();

        /// <summary>暴露释放后的 Actor，复现 ObjectDisposedException</summary>
        public async Task DisposeRegistryForTestAsync() => await _requestRegistry.DisposeAsync();

        /// <summary>暴露 ProcessResponseAsync（当前调用方以裸语句丢弃其 Task）</summary>
        public Task ProcessResponseForTest(JsonRpcResponse response) => ProcessResponseAsync(response);

        /// <summary>暴露安全的 fire-and-forget 包裹器</summary>
        public Task FireAndForgetProcessResponseForTest(JsonRpcResponse response) => FireAndForgetProcessResponseAsync(response);

        /// <summary>注册 pending request 并返回其 TCS,模拟 SendRequestAsync 注册等待</summary>
        public async Task<TaskCompletionSource<JsonRpcResponse>> RegisterPendingForTestAsync(int requestId)
        {
            var tcs = new TaskCompletionSource<JsonRpcResponse>();
            await _requestRegistry.RegisterAsync(requestId, tcs);
            return tcs;
        }
    }

    private static JsonRpcResponse CreateResponse(long id) => new() { Id = JsonRpcId.FromNumber(id) };

    [Fact]
    public async Task ProcessResponseAsync_AfterActorDisposed_ThrowsObjectDisposedException()
    {
        var client = new TestClient();
        await client.DisposeRegistryForTestAsync().ConfigureAwait(true);

        var act = async () => await client.ProcessResponseForTest(CreateResponse(1)).ConfigureAwait(true);
        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task FireAndForgetProcessResponseAsync_AfterActorDisposed_DoesNotThrow()
    {
        var client = new TestClient();
        await client.DisposeRegistryForTestAsync().ConfigureAwait(true);

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
    /// 验证 Actor 模型下 ProcessResponseAsync 不会死锁。
    /// Actor 单消费者 Channel 串行处理，TrySetResult 在 Consumer 线程执行，无锁竞争。
    /// </summary>
    [Fact]
    public async Task ProcessResponseAsync_ActorModel_DoesNotDeadlock()
    {
        var client = new TestClient();
        var tcs = await client.RegisterPendingForTestAsync(1);

        await client.ProcessResponseForTest(CreateResponse(1)).WaitAsync(TimeSpan.FromSeconds(5));

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        response.Should().NotBeNull("Actor 应在 5s 内完成 pending request");
    }
}
