namespace Core.Utils;

/// <summary>
/// BusTransport 单元测试 — 验证总线拓扑属性、启动、主机选举、回环发送。
/// </summary>
public class BusTransportTest {
    [Fact]
    public async Task Kind_ReturnsBus() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.Kind.Should().Be(TransportTopology.Bus);
    }

    [Fact]
    public async Task ProcessId_ReturnsCurrentPid() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.ProcessId.Should().Be(Environment.ProcessId.ToString());
    }

    [Fact]
    public async Task IsRunning_FalseBeforeStart() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task IsRunning_TrueAfterStart() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        await transport.StartAsync();
        transport.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_NoExistingHost_BecomesHost() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        await transport.StartAsync();
        transport.Role.Should().Be(ProcessRole.Host);
        transport.HostProcessId.Should().Be(transport.ProcessId);
    }

    [Fact]
    public async Task GetConnectedProcesses_EmptyBeforeStart() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.GetConnectedProcesses().Should().BeEmpty();
    }

    [Fact]
    public async Task GetConnectedProcesses_EmptyAfterHostStart_NoSlaves() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        await transport.StartAsync();
        await Task.Delay(100);
        transport.GetConnectedProcesses().Should().BeEmpty();
    }

    [Fact]
    public async Task BroadcastAsync_HostMode_NoSlaves_CompletesWithoutError() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        await transport.StartAsync();

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("bus-broadcast"));
        await transport.BroadcastAsync(data);
    }

    [Fact]
    public async Task SendAsync_HostMode_ToSelf_LoopsBack() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        await transport.StartAsync();

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("self-send"));
        await transport.SendAsync(transport.ProcessId, data);

        var frame = await transport.ReceiveAsync().FirstAsync();
        frame.SourceProcessId.Should().Be(transport.ProcessId);
        Encoding.UTF8.GetString(frame.Data.Span).Should().Be("self-send");
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes() {
        var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        await transport.DisposeAsync();
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Election_ReturnsElectionService() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.Election.Should().NotBeNull();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_DoesNotCrash() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await transport.StartAsync(cts.Token);
        await transport.StartAsync(cts.Token);
        transport.IsRunning.Should().BeTrue();
    }
}