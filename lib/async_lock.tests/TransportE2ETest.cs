namespace Core.Utils;

/// <summary>
/// 传输层 E2E 集成测试 — 同进程内模拟两个进程，通过有名管道实际通信。
/// <para>用可注入 PID 创建两个 transport 实例，验证完整握手→选举→消息传递链路。</para>
/// </summary>
public class TransportE2ETest
{
    private static async Task<TransportFrame?> ReceiveWithTimeoutAsync(
        IAsyncEnumerable<TransportFrame> source,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try { return await source.FirstOrDefaultAsync(cts.Token); }
        catch (OperationCanceledException) { return null; }
    }

    [Fact]
    public async Task BusTransport_HostSlave_MessageRoundTrip()
    {
        var pipeName = $"e2e-bus-{Guid.NewGuid():N}";
        var hostPid = "100001";
        var slavePid = "100002";

        await using var host = new BusTransport(pipeName: pipeName, processId: hostPid);
        await using var slave = new BusTransport(pipeName: pipeName, processId: slavePid);

        await host.StartAsync();
        await Task.Delay(500);
        host.Role.Should().Be(ProcessRole.Host, "第一个启动应为主机");

        await slave.StartAsync();
        await Task.Delay(500);

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("e2e-bus-message"));
        await host.BroadcastAsync(data);

        var received = await ReceiveWithTimeoutAsync(slave.ReceiveAsync(), TimeSpan.FromSeconds(5));
        received.Should().NotBeNull();
        Encoding.UTF8.GetString(received!.Data.Span).Should().Be("e2e-bus-message");
        received.SourceProcessId.Should().Be(hostPid);
    }

    [Fact]
    public async Task BusTransport_TwoInstances_HostElectionWorks()
    {
        var pipeName = $"e2e-bus-elect-{Guid.NewGuid():N}";
        var lowPid = "200001";
        var highPid = "200002";

        await using var transport1 = new BusTransport(pipeName: pipeName, processId: lowPid);
        await using var transport2 = new BusTransport(pipeName: pipeName, processId: highPid);

        await transport1.StartAsync();
        await Task.Delay(300);

        await transport2.StartAsync();
        await Task.Delay(300);

        transport1.Role.Should().Be(ProcessRole.Host, "先启动的应为主机");
        transport2.Role.Should().Be(ProcessRole.Slave, "后启动的应为从机");
        transport2.HostProcessId.Should().Be(lowPid, "从机应知道主机 PID");
    }

    [Fact]
    public async Task MeshTransport_TwoPeers_PointToPointMessage()
    {
        var baseName = $"e2e-mesh-{Guid.NewGuid():N}";
        var pid1 = "300001";
        var pid2 = "300002";

        await using var peer1 = new MeshTransport(basePipeName: baseName, processId: pid1);
        await using var peer2 = new MeshTransport(basePipeName: baseName, processId: pid2);

        await peer1.StartAsync();
        await peer2.StartAsync();

        var connected = await peer1.AddPeerAsync(pid2, CancellationToken.None);
        connected.Should().BeTrue("应能连接到 peer2");

        await Task.Delay(100);

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("mesh-p2p-msg"));
        await peer1.SendAsync(pid2, data);

        var received = await ReceiveWithTimeoutAsync(peer2.ReceiveAsync(), TimeSpan.FromSeconds(5));
        received.Should().NotBeNull();
        Encoding.UTF8.GetString(received!.Data.Span).Should().Be("mesh-p2p-msg");
        received.SourceProcessId.Should().Be(pid1);
    }

    [Fact]
    public async Task MeshTransport_Bidirectional_BothDirectionsWork()
    {
        var baseName = $"e2e-mesh-bi-{Guid.NewGuid():N}";
        var pid1 = "400001";
        var pid2 = "400002";

        await using var peer1 = new MeshTransport(basePipeName: baseName, processId: pid1);
        await using var peer2 = new MeshTransport(basePipeName: baseName, processId: pid2);

        await peer1.StartAsync();
        await peer2.StartAsync();

        await peer1.AddPeerAsync(pid2);
        await peer2.AddPeerAsync(pid1);

        var msg1 = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("1->2"));
        var msg2 = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("2->1"));

        await peer1.SendAsync(pid2, msg1);
        await peer2.SendAsync(pid1, msg2);

        var recv2 = await ReceiveWithTimeoutAsync(peer2.ReceiveAsync(), TimeSpan.FromSeconds(5));
        var recv1 = await ReceiveWithTimeoutAsync(peer1.ReceiveAsync(), TimeSpan.FromSeconds(5));

        recv2.Should().NotBeNull();
        Encoding.UTF8.GetString(recv2!.Data.Span).Should().Be("1->2");

        recv1.Should().NotBeNull();
        Encoding.UTF8.GetString(recv1!.Data.Span).Should().Be("2->1");
    }

    [Fact]
    public async Task NamedPipeTransport_HostSlave_MessageRoundTrip()
    {
        var pipeName = $"e2e-np-{Guid.NewGuid():N}";
        var hostPid = "500001";
        var slavePid = "500002";

        await using var host = new NamedPipeTransport(pipeName: pipeName, processId: hostPid);
        await using var slave = new NamedPipeTransport(pipeName: pipeName, processId: slavePid);

        await host.StartAsync();
        await Task.Delay(300);

        await slave.StartAsync();
        await Task.Delay(300);

        host.Role.Should().Be(ProcessRole.Host);
        slave.Role.Should().Be(ProcessRole.Slave);

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("np-e2e-msg"));
        await host.BroadcastAsync(data);

        var received = await ReceiveWithTimeoutAsync(slave.ReceiveAsync(), TimeSpan.FromSeconds(5));
        received.Should().NotBeNull();
        Encoding.UTF8.GetString(received!.Data.Span).Should().Be("np-e2e-msg");
        received.SourceProcessId.Should().Be(hostPid);
    }

    [Fact]
    public async Task BusTransport_Broadcast_MultipleSlavesReceive()
    {
        var pipeName = $"e2e-bus-multi-{Guid.NewGuid():N}";
        var hostPid = "600001";
        var slave1Pid = "600002";
        var slave2Pid = "600003";

        await using var host = new BusTransport(pipeName: pipeName, processId: hostPid);
        await using var slave1 = new BusTransport(pipeName: pipeName, processId: slave1Pid);
        await using var slave2 = new BusTransport(pipeName: pipeName, processId: slave2Pid);

        await host.StartAsync();
        await Task.Delay(300);
        await slave1.StartAsync();
        await Task.Delay(300);
        await slave2.StartAsync();
        await Task.Delay(300);

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("broadcast-all"));
        await host.BroadcastAsync(data);

        var recv1 = await ReceiveWithTimeoutAsync(slave1.ReceiveAsync(), TimeSpan.FromSeconds(5));
        var recv2 = await ReceiveWithTimeoutAsync(slave2.ReceiveAsync(), TimeSpan.FromSeconds(5));

        recv1.Should().NotBeNull("slave1 应收到广播");
        Encoding.UTF8.GetString(recv1!.Data.Span).Should().Be("broadcast-all");

        recv2.Should().NotBeNull("slave2 应收到广播");
        Encoding.UTF8.GetString(recv2!.Data.Span).Should().Be("broadcast-all");
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Condition not met within {timeout.TotalSeconds}s");
    }
}
