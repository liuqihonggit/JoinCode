namespace Core.Utils;

/// <summary>
/// MeshTransport 单元测试 — 验证网状拓扑属性、启动、回环发送、连接管理。
/// </summary>
public class MeshTransportTest {
    [Fact]
    public async Task Kind_ReturnsMesh() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        transport.Kind.Should().Be(TransportTopology.Mesh);
    }

    [Fact]
    public async Task Role_AlwaysHost() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        transport.Role.Should().Be(ProcessRole.Host);
    }

    [Fact]
    public async Task ProcessId_ReturnsCurrentPid() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        transport.ProcessId.Should().Be(Environment.ProcessId.ToString());
    }

    [Fact]
    public async Task HostProcessId_EqualsProcessId() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        transport.HostProcessId.Should().Be(transport.ProcessId);
    }

    [Fact]
    public async Task IsRunning_FalseBeforeStart() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        transport.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task IsRunning_TrueAfterStart() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        await transport.StartAsync();
        transport.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task MyPipeName_CorrectFormat() {
        var baseName = $"test-mesh-{Guid.NewGuid():N}";
        await using var transport = new MeshTransport(basePipeName: baseName);
        transport.MyPipeName.Should().Be($"{baseName}-{Environment.ProcessId}");
    }

    [Fact]
    public async Task GetPeerPipeName_CorrectFormat() {
        var baseName = $"test-mesh-{Guid.NewGuid():N}";
        await using var transport = new MeshTransport(basePipeName: baseName);
        transport.GetPeerPipeName("12345").Should().Be($"{baseName}-12345");
    }

    [Fact]
    public async Task GetConnectedProcesses_EmptyBeforeAnyConnection() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        transport.GetConnectedProcesses().Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_ToSelf_LoopsBackToReceive() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        await transport.StartAsync();

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("loopback-test"));
        await transport.SendAsync(transport.ProcessId, data);

        var frame = await transport.ReceiveAsync().FirstAsync();
        frame.SourceProcessId.Should().Be(transport.ProcessId);
        Encoding.UTF8.GetString(frame.Data.Span).Should().Be("loopback-test");
    }

    [Fact]
    public async Task BroadcastAsync_NoPeers_CompletesWithoutError() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        await transport.StartAsync();

        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("broadcast-test"));
        await transport.BroadcastAsync(data);
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes() {
        var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        await transport.DisposeAsync();
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_DoesNotCrash() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await transport.StartAsync(cts.Token);
        await transport.StartAsync(cts.Token);
        transport.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task AddPeerAsync_ToSelf_ReturnsTrue() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        await transport.StartAsync();
        var result = await transport.AddPeerAsync(transport.ProcessId);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task AddPeerAsync_ToNonExistent_ReturnsFalse() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        await transport.StartAsync();
        var result = await transport.AddPeerAsync("99999999");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ToNonExistentPeer_DoesNotThrow() {
        await using var transport = new MeshTransport(basePipeName: $"test-mesh-{Guid.NewGuid():N}");
        await transport.StartAsync();
        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("test"));
        var act = async () => await transport.SendAsync("99999999", data);
        await act.Should().NotThrowAsync();
    }
}