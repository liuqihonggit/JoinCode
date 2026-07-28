
namespace Bridge.Tests.Phase7D;

public sealed class BridgeHandleTests
{
    public BridgeHandleTests()
    {
        // 每个测试前清理全局句柄
        BridgeHandle.SetHandle(null);
    }

    [Fact]
    public void GetHandle_Default_ReturnsNull()
    {
        var handle = BridgeHandle.GetHandle();
        Assert.Null(handle);
    }

    [Fact]
    public void SetHandle_SetsValue()
    {
        var mockHandle = new TestBridgeHandle { SessionId = "cse_test123" };
        BridgeHandle.SetHandle(mockHandle);

        var result = BridgeHandle.GetHandle();
        Assert.NotNull(result);
        Assert.Equal("cse_test123", result.SessionId);
    }

    [Fact]
    public void SetHandle_Null_Clears()
    {
        var mockHandle = new TestBridgeHandle { SessionId = "cse_test123" };
        BridgeHandle.SetHandle(mockHandle);
        BridgeHandle.SetHandle(null);

        Assert.Null(BridgeHandle.GetHandle());
    }

    [Fact]
    public void GetSelfCompatId_WithHandle_ReturnsCompatId()
    {
        var mockHandle = new TestBridgeHandle { SessionId = "cse_test123" };
        BridgeHandle.SetHandle(mockHandle);

        var compatId = BridgeHandle.GetSelfCompatId();
        Assert.Equal("session_test123", compatId);
    }

    [Fact]
    public void GetSelfCompatId_NoHandle_ReturnsNull()
    {
        var compatId = BridgeHandle.GetSelfCompatId();
        Assert.Null(compatId);
    }

    /// <summary>测试用桥句柄</summary>
    private sealed class TestBridgeHandle : IReplBridgeHandle
    {
        public required string SessionId { get; init; }
        public string EnvironmentId { get; } = string.Empty;
        public string SessionIngressUrl { get; } = string.Empty;
        public JoinCode.Transport.Bridge.BridgeState State => JoinCode.Transport.Bridge.BridgeState.Connected;
        public void WriteMessages(string[] messages) { }
        public void WriteSdkMessages(string[] messages) { }
        public void SendControlRequest(string requestJson) { }
        public void SendControlResponse(string responseJson) { }
        public void SendControlCancelRequest(string requestId) { }
        public void SendResult() { }
        public Task TeardownAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public int GetSSESequenceNum() => 0;
    }
}
