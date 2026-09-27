namespace Core.Utils;

/// <summary>
/// BusTransport 单元测试 — 验证总线拓扑属性、启动、主机选举、回环发送。
/// </summary>
public class BusTransportTest {
    /// <summary>验证拓扑类型返回 Bus</summary>
    [Fact]
    public async Task Kind_ReturnsBus() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.Kind.Should().Be(TransportTopology.Bus);
    }

    /// <summary>验证进程标识返回当前进程 PID</summary>
    [Fact]
    public async Task ProcessId_ReturnsCurrentPid() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.ProcessId.Should().Be(Environment.ProcessId.ToString());
    }

    /// <summary>验证启动前运行状态为假</summary>
    [Fact]
    public async Task IsRunning_FalseBeforeStart() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.IsRunning.Should().BeFalse();
    }

    /// <summary>验证启动后运行状态为真</summary>
    [Fact]
    public async Task IsRunning_TrueAfterStart() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        await transport.StartAsync();
        transport.IsRunning.Should().BeTrue();
    }

    /// <summary>验证无现存主机时启动后自身成为主机</summary>
    [Fact]
    public async Task StartAsync_NoExistingHost_BecomesHost() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        await transport.StartAsync();
        transport.Role.Should().Be(ProcessRole.Host);
        transport.HostProcessId.Should().Be(transport.ProcessId);
    }

    /// <summary>验证启动前已连接进程列表为空</summary>
    [Fact]
    public async Task GetConnectedProcesses_EmptyBeforeStart() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.GetConnectedProcesses().Should().BeEmpty();
    }

    /// <summary>验证主机启动且无从节点时已连接进程列表为空</summary>
    [Fact]
    public async Task GetConnectedProcesses_EmptyAfterHostStart_NoSlaves() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        await transport.StartAsync();
        await Task.Delay(100);
        transport.GetConnectedProcesses().Should().BeEmpty();
    }

    /// <summary>验证主机模式无从节点时广播能无错完成</summary>
    [Fact]
    public async Task BroadcastAsync_HostMode_NoSlaves_CompletesWithoutError() {
        var uniquePipe = $"test-bus-{Guid.NewGuid():N}";
        await using var transport = new BusTransport(pipeName: uniquePipe);
        await transport.StartAsync();

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("bus-broadcast"));
        await transport.BroadcastAsync(data);
    }

    /// <summary>验证主机模式向自身发送消息会回环投递</summary>
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

    /// <summary>验证异步释放可被多次调用而不抛异常</summary>
    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes() {
        var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        await transport.DisposeAsync();
        await transport.DisposeAsync();
    }

    /// <summary>验证选举服务实例不为空</summary>
    [Fact]
    public async Task Election_ReturnsElectionService() {
        await using var transport = new BusTransport(pipeName: $"test-bus-{Guid.NewGuid():N}");
        transport.Election.Should().NotBeNull();
    }

    /// <summary>验证重复调用启动不会崩溃</summary>
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