namespace Composition.Tests.Commands;

/// <summary>
/// /peers 命令单元测试 — 覆盖服务缺失静默/空节点列表提示/已连接节点渲染(名称+ID+连接时间)
/// <para>IPeerDiscoveryService 经 DI 注入</para>
/// </summary>
[Collection("command-output")]
public sealed class PeersCommandTest {
    private static PeerInfo CreatePeer(string id = "peer-1", string name = "node-a") => new() {
        Id = id,
        Name = name,
        ConnectedAt = new DateTime(2026, 1, 1, 9, 30, 0),
    };

    private static (PeersCommand Command, Mock<IPeerDiscoveryService> PeerService, ChatCommandContext Context) Arrange(
        PeerInfo[]? peers = null,
        bool withService = true) {
        var peerService = new Mock<IPeerDiscoveryService>();
        peerService.Setup(p => p.GetConnectedPeers()).Returns(peers ?? []);

        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                if (withService) di.AddSingleton(peerService.Object);
            });
        var context = CommandTestHarness.CreateContext(services);
        return (new PeersCommand(), peerService, context);
    }

    [Fact]
    public async Task Execute_WithoutService_NonInteractive_ReturnsContinueSilently() {
        var (cmd, peerService, context) = Arrange(withService: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().NotContain("对等节点列表", "非交互环境服务缺失不应输出节点列表");
        peerService.Verify(p => p.GetConnectedPeers(), Times.Never);
    }

    [Fact]
    public async Task Execute_NoConnectedPeers_ShowsEmptyHint() {
        var (cmd, peerService, context) = Arrange();

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        peerService.Verify(p => p.GetConnectedPeers(), Times.Once);
        output.Should().Contain("对等节点列表:");
        output.Should().Contain("(暂无已连接的对等节点)");
    }

    [Fact]
    public async Task Execute_WithConnectedPeers_RendersPeerNameIdAndTime() {
        var (cmd, _, context) = Arrange(peers: [
            CreatePeer(id: "peer-1", name: "node-a"),
            CreatePeer(id: "peer-2", name: "node-b")]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("node-a (peer-1)");
        output.Should().Contain("node-b (peer-2)");
        output.Should().Contain("连接时间: 09:30:00");
        output.Should().NotContain("暂无已连接的对等节点");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new PeersCommand();
        cmd.Name.Should().Be("peers");
        cmd.Usage.Should().Be("/peers");
        cmd.Aliases.Should().Contain("remote");
        cmd.IsHidden.Should().BeTrue();
    }
}
