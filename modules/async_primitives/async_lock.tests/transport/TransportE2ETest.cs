// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Core.Utils;

/// <summary>
/// 传输层 E2E 集成测试 — 同进程内模拟两个进程，通过有名管道实际通信。
/// <para>用可注入 PID 创建两个 transport 实例，验证完整握手→选举→消息传递链路。</para>
/// </summary>
public class TransportE2ETest {
    private static async Task<TransportFrame?> ReceiveWithTimeoutAsync(
        IAsyncEnumerable<TransportFrame> source,
        TimeSpan timeout) {
        using var cts = new CancellationTokenSource(timeout);
        try { return await source.FirstOrDefaultAsync(cts.Token); } catch (OperationCanceledException) { return null; }
    }

    /// <summary>
    /// 等待 host 的已连接进程表收敛到包含全部期望 PID（状态收敛轮询，非固定延迟祈祷）。
    /// BroadcastAsync 只对「此刻已入 _clientConnections」的 slave 发包（fire-and-forget 无回放），
    /// 而 slave StartAsync 返回 ≠ 握手完成入表 — 固定 Task.Delay 在慢机（CI）上不够，
    /// 曾致 BusTransport_Broadcast_MultipleSlavesReceive slave2 收不到广播（599/600 flaky）。
    /// </summary>
    private static async Task WaitForConnectedAsync(
        BusTransport host, IReadOnlyList<string> expectedPids, TimeSpan timeout) {
        using var cts = new CancellationTokenSource(timeout);
        while (!expectedPids.All(host.GetConnectedProcesses().Contains)) {
            await Task.Delay(50, cts.Token);
        }
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task BusTransport_HostSlave_MessageRoundTrip() {
        var pipeName = $"e2e-bus-{Guid.NewGuid():N}";
        var hostPid = "100001";
        var slavePid = "100002";

        await using var host = new BusTransport(pipeName: pipeName, processId: hostPid);
        await using var slave = new BusTransport(pipeName: pipeName, processId: slavePid);

        await host.StartAsync();
        await TestWaitHelper.WaitUntilAsync(() => Task.FromResult(host.Role == ProcessRole.Host), TimeSpan.FromSeconds(2));
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
    public async Task BusTransport_TwoInstances_HostElectionWorks() {
        var pipeName = $"e2e-bus-elect-{Guid.NewGuid():N}";
        var lowPid = "200001";
        var highPid = "200002";

        await using var transport1 = new BusTransport(pipeName: pipeName, processId: lowPid);
        await using var transport2 = new BusTransport(pipeName: pipeName, processId: highPid);

        await transport1.StartAsync();
        await TestWaitHelper.WaitUntilAsync(() => Task.FromResult(transport1.Role == ProcessRole.Host), TimeSpan.FromSeconds(2));

        await transport2.StartAsync();
        await TestWaitHelper.WaitUntilAsync(() => Task.FromResult(transport2.Role == ProcessRole.Slave), TimeSpan.FromSeconds(2));

        transport1.Role.Should().Be(ProcessRole.Host, "先启动的应为主机");
        transport2.Role.Should().Be(ProcessRole.Slave, "后启动的应为从机");
        transport2.HostProcessId.Should().Be(lowPid, "从机应知道主机 PID");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task MeshTransport_TwoPeers_PointToPointMessage() {
        // 诊断日志开启（finally 复位）：CI 失败时 stderr 输出 PIPE-ACCEPT/MESH/MESH-WRITE 全链，
        // 丢帧断点直接可定位（TransportDiagnostics.Enabled 是进程级静态，仅本测试窗口内开启）
        TransportDiagnostics.Enabled = true;
        try {
            var baseName = $"e2e-mesh-{Guid.NewGuid():N}";
            var pid1 = "300001";
            var pid2 = "300002";

            await using var peer1 = new MeshTransport(basePipeName: baseName, processId: pid1);
            await using var peer2 = new MeshTransport(basePipeName: baseName, processId: pid2);

            await peer1.StartAsync();
            await peer2.StartAsync();

            var connected = await peer1.AddPeerAsync(pid2, CancellationToken.None);
            connected.Should().BeTrue("应能连接到 peer2");

            // 确定性发送：立即发送（对齐同文件双向测试的 CI 绿形态 — AddPeer 后无需延时等待；
            // 数据经 peer1 连接缓存直写，peer2 侧读循环由管道内核缓冲保底，原先的 Task.Delay(100)
            // 是时间耦合反模式：Windows 上碰巧够、Linux CI 慢机上无正确性保证）
            var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("mesh-p2p-msg"));
            await peer1.SendAsync(pid2, data);

            var received = await ReceiveWithTimeoutAsync(peer2.ReceiveAsync(), TimeSpan.FromSeconds(5));
            received.Should().NotBeNull();
            Encoding.UTF8.GetString(received!.Data.Span).Should().Be("mesh-p2p-msg");
            received.SourceProcessId.Should().Be(pid1);
        } finally {
            TransportDiagnostics.Enabled = false;
        }
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task MeshTransport_Bidirectional_BothDirectionsWork() {
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

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task NamedPipeTransport_HostSlave_MessageRoundTrip() {
        var pipeName = $"e2e-np-{Guid.NewGuid():N}";
        var hostPid = "500001";
        var slavePid = "500002";

        await using var host = new NamedPipeTransport(pipeName: pipeName, processId: hostPid);
        await using var slave = new NamedPipeTransport(pipeName: pipeName, processId: slavePid);

        await host.StartAsync();
        await TestWaitHelper.WaitUntilAsync(() => Task.FromResult(host.Role == ProcessRole.Host), TimeSpan.FromSeconds(2));

        await slave.StartAsync();
        await TestWaitHelper.WaitUntilAsync(() => Task.FromResult(slave.Role == ProcessRole.Slave), TimeSpan.FromSeconds(2));
        await Task.Delay(200);

        host.Role.Should().Be(ProcessRole.Host);
        slave.Role.Should().Be(ProcessRole.Slave);

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("np-e2e-msg"));
        await host.BroadcastAsync(data);

        TransportFrame? received = null;
        await TestWaitHelper.WaitUntilAsync(async () => {
            received = await ReceiveWithTimeoutAsync(slave.ReceiveAsync(), TimeSpan.FromMilliseconds(500));
            return received is not null;
        }, TimeSpan.FromMilliseconds(500));
        received.Should().NotBeNull();
        Encoding.UTF8.GetString(received!.Data.Span).Should().Be("np-e2e-msg");
        received.SourceProcessId.Should().Be(hostPid);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    [Trait("Category", "Flaky")]
    public async Task BusTransport_Broadcast_MultipleSlavesReceive() {
        var pipeName = $"e2e-bus-multi-{Guid.NewGuid():N}";
        var hostPid = "600001";
        var slave1Pid = "600002";
        var slave2Pid = "600003";

        // Linux CI 确定性失败取证（本地 Windows 恒绿无法复现）：开 TransportDiagnostics，
        // 失败时 xUnit 会把 stderr 全轨迹（PIPE-ACCEPT/BUS 收发）带进失败输出 — 一次定位根因
        TransportDiagnostics.Enabled = true;
        try {
        await using var host = new BusTransport(pipeName: pipeName, processId: hostPid);
        await using var slave1 = new BusTransport(pipeName: pipeName, processId: slave1Pid);
        await using var slave2 = new BusTransport(pipeName: pipeName, processId: slave2Pid);

        await host.StartAsync();
        await slave1.StartAsync();
        await slave2.StartAsync();
        // 确定性等待两个 slave 握手入表（BroadcastAsync 只发已连接者，无回放 — 参见 WaitForConnectedAsync 注释）
        await WaitForConnectedAsync(host, [slave1Pid, slave2Pid], TimeSpan.FromSeconds(10));

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("broadcast-all"));
        await host.BroadcastAsync(data);

        var recv1 = await ReceiveWithTimeoutAsync(slave1.ReceiveAsync(), TimeSpan.FromSeconds(5));
        var recv2 = await ReceiveWithTimeoutAsync(slave2.ReceiveAsync(), TimeSpan.FromSeconds(5));

        // 现场快照：广播后 host 连接表 + 各 slave 实收帧（失败时直接看丢在哪一环）
        var scene = $"广播后host表=[{string.Join(",", host.GetConnectedProcesses())}] "
            + $"slave1实收={DescribeFrame(recv1)} slave2实收={DescribeFrame(recv2)}";

        recv1.Should().NotBeNull($"slave1 应收到广播；现场: {scene}");
        Encoding.UTF8.GetString(recv1!.Data.Span).Should().Be("broadcast-all");

        recv2.Should().NotBeNull($"slave2 应收到广播；现场: {scene}");
        Encoding.UTF8.GetString(recv2!.Data.Span).Should().Be("broadcast-all");
        } finally {
            TransportDiagnostics.Enabled = false;
        }
    }

    /// <summary>帧摘要（null 显式标注）— 断言失败现场快照用</summary>
    private static string DescribeFrame(TransportFrame? frame) =>
        frame is null ? "<null>" : $"{frame.SourceProcessId}:{Encoding.UTF8.GetString(frame.Data.Span)}";
}