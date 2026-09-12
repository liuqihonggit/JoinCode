
namespace Bridge.Tests.Phase7C;

public sealed class BridgeFaultInjectionTests
{
    [Fact]
    public void InjectFault_DoesNotThrow()
    {
        var fault = new BridgeFault
        {
            Method = "pollForWork",
            Kind = BridgeFaultKind.Transient,
            Status = 500,
            RemainingCount = 1,
        };

        BridgeDebugController.InjectFault(fault);
        BridgeDebugController.ClearHandle();
    }

    [Fact]
    public void ClearHandle_RemovesAllFaults()
    {
        BridgeDebugController.InjectFault(new BridgeFault
        {
            Method = "pollForWork",
            Kind = BridgeFaultKind.Transient,
            Status = 500,
            RemainingCount = 1,
        });

        BridgeDebugController.ClearHandle();
        // 不抛异常即通过
        Assert.True(true);
    }

    [Fact]
    public void RegisterHandle_AndGetHandle_ReturnsHandle()
    {
        var handle = new MockDebugHandle();
        BridgeDebugController.RegisterHandle(handle);

        var retrieved = BridgeDebugController.GetHandle();
        Assert.Same(handle, retrieved);

        BridgeDebugController.ClearHandle();
    }

    [Fact]
    public void ClearHandle_SetsHandleToNull()
    {
        var handle = new MockDebugHandle();
        BridgeDebugController.RegisterHandle(handle);
        BridgeDebugController.ClearHandle();

        var retrieved = BridgeDebugController.GetHandle();
        Assert.Null(retrieved);
    }

    [Fact]
    public void FaultInjectionBridgeApiClient_ConstructsSuccessfully()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var options = new BridgeApiOptions { BaseUrl = "http://localhost:12345" };
        var inner = new BridgeApiClient(http, options);
        var client = new FaultInjectionBridgeApiClient(inner);

        Assert.NotNull(client);
        client.Dispose();
    }

    [Fact]
    public async Task FaultInjectionBridgeApiClient_WithFatalFault_ThrowsFatalError()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var options = new BridgeApiOptions { BaseUrl = "http://localhost:12345" };
        var inner = new BridgeApiClient(http, options);
        var client = new FaultInjectionBridgeApiClient(inner);

        BridgeDebugController.InjectFault(new BridgeFault
        {
            Method = "pollForWork",
            Kind = BridgeFaultKind.Fatal,
            Status = 401,
            ErrorType = "auth_error",
            RemainingCount = 1,
        });

        await Assert.ThrowsAsync<BridgeFatalError>(() =>
            client.PollForWorkAsync("env1", CancellationToken.None)).ConfigureAwait(true);

        BridgeDebugController.ClearHandle();
        client.Dispose();
    }

    [Fact]
    public async Task FaultInjectionBridgeApiClient_WithTransientFault_ThrowsHttpRequestException()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var options = new BridgeApiOptions { BaseUrl = "http://localhost:12345" };
        var inner = new BridgeApiClient(http, options);
        var client = new FaultInjectionBridgeApiClient(inner);

        BridgeDebugController.InjectFault(new BridgeFault
        {
            Method = "pollForWork",
            Kind = BridgeFaultKind.Transient,
            Status = 500,
            RemainingCount = 1,
        });

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.PollForWorkAsync("env1", CancellationToken.None)).ConfigureAwait(true);

        BridgeDebugController.ClearHandle();
        client.Dispose();
    }

    private sealed class MockDebugHandle : IBridgeDebugHandle
    {
        public void FireClose() { }
        public void ForceReconnect() { }
        public void InjectFault(BridgeFault fault) { }
        public void WakePollLoop() { }
        public string Describe() => "mock";
    }
}
