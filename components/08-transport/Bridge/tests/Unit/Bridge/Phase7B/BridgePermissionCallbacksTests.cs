
namespace Bridge.Tests.Phase7B;

public sealed class BridgePermissionCallbacksTests
{
    [Fact]
    public void IsBridgePermissionResponse_AllowBehavior_ReturnsTrue()
    {
        var json = """{"behavior":"allow"}""";
        var je = JsonDocument.Parse(json).RootElement;

        Assert.True(BridgePermissionCallbackService.IsBridgePermissionResponse(je));
    }

    [Fact]
    public void IsBridgePermissionResponse_DenyBehavior_ReturnsTrue()
    {
        var json = """{"behavior":"deny"}""";
        var je = JsonDocument.Parse(json).RootElement;

        Assert.True(BridgePermissionCallbackService.IsBridgePermissionResponse(je));
    }

    [Fact]
    public void IsBridgePermissionResponse_OtherBehavior_ReturnsFalse()
    {
        var json = """{"behavior":"ask"}""";
        var je = JsonDocument.Parse(json).RootElement;

        Assert.False(BridgePermissionCallbackService.IsBridgePermissionResponse(je));
    }

    [Fact]
    public void IsBridgePermissionResponse_NonObject_ReturnsFalse()
    {
        var json = """["not","an","object"]""";
        var je = JsonDocument.Parse(json).RootElement;

        Assert.False(BridgePermissionCallbackService.IsBridgePermissionResponse(je));
    }

    [Fact]
    public async Task OnResponse_RegistersAndFiresHandler()
    {
        var transport = new MockTransport();
        var service = new BridgePermissionCallbackService(transport);

        var fired = false;
        var unsub = service.OnResponse("req1", _ => { fired = true; return Task.CompletedTask; });

        var response = new PermissionCallbackResponse
        {
            Behavior = PermissionBehaviorConstants.Allow,
        };
        await service.HandleResponseAsync("req1", response).ConfigureAwait(true);

        Assert.True(fired);

        // 取消订阅后不再触发
        unsub();
        fired = false;
        await service.HandleResponseAsync("req1", response).ConfigureAwait(true);
        Assert.False(fired);
    }

    [Fact]
    public async Task HandleResponse_UnknownRequestId_DoesNotThrow()
    {
        var transport = new MockTransport();
        var service = new BridgePermissionCallbackService(transport);

        var response = new PermissionCallbackResponse
        {
            Behavior = PermissionBehaviorConstants.Allow,
        };

        // 不应抛异常
        await service.HandleResponseAsync("unknown", response).ConfigureAwait(true);
    }

    [Fact]
    public void SendRequest_WritesToTransport()
    {
        var transport = new MockTransport();
        var service = new BridgePermissionCallbackService(transport);

        service.SendRequest("req1", "ReadFile", new Dictionary<string, JsonElement>(),
            "tool1", "test description");

        Assert.Single(transport.WrittenMessages);
        Assert.Contains("permission_request", transport.WrittenMessages[0]);
    }

    [Fact]
    public void CancelRequest_WritesToTransport()
    {
        var transport = new MockTransport();
        var service = new BridgePermissionCallbackService(transport);

        service.CancelRequest("req1");

        Assert.Single(transport.WrittenMessages);
        Assert.Contains("permission_cancel", transport.WrittenMessages[0]);
    }

    /// <summary>模拟传输层</summary>
    private sealed class MockTransport : IReplBridgeTransport
    {
        public List<string> WrittenMessages { get; } = [];
        public int DroppedBatchCount => 0;

        public Task WriteAsync(string message, CancellationToken ct = default)
        {
            WrittenMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<string> messages, CancellationToken ct = default)
        {
            foreach (var m in messages) WrittenMessages.Add(m);
            return Task.CompletedTask;
        }

        public void Close() { }
        public string GetStateLabel() => "mock";
        public bool IsConnectedStatus() => true;
        public void SetOnData(Action<string> callback) { }
        public void SetOnClose(Action<int?> callback) { }
        public void SetOnConnect(Action callback) { }
        public void SetOnBatchDropped(Action<int, int> callback) { }
        public void Connect() { }
        public int GetLastSequenceNum() => 0;
        public Task ReportStateAsync(BridgeSessionActivity state, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReportMetadataAsync(Dictionary<string, JsonElement> metadata, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReportDeliveryAsync(string eventId, string status, CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
