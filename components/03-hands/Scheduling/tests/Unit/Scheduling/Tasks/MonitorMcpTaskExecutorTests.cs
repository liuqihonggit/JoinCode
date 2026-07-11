
#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Sync.Tests.Scheduling.Tasks;

public class MonitorMcpTaskExecutorTests : IAsyncDisposable
{
    private readonly Mock<IMcpToolRegistry> _mcpToolRegistryMock;
    private readonly MonitorMcpTaskExecutor _executor;

    public MonitorMcpTaskExecutorTests()
    {
        _mcpToolRegistryMock = new Mock<IMcpToolRegistry>();
        _executor = new MonitorMcpTaskExecutor(
            _mcpToolRegistryMock.Object,
            NullLogger<MonitorMcpTaskExecutor>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _executor.DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task StartMonitoringAsync_ShouldReturnMonitorId()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var mcpClientMock = new Mock<IMcpClient>();
        mcpClientMock.SetupGet(x => x.IsConnected).Returns(true);
        mcpClientMock
            .Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpListToolsResult(true, Array.Empty<JoinCode.Abstractions.Tools.ToolInfo>()));
        mcpClientMock
            .Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpListResourcesResult(true, Array.Empty<McpResource>()));

        var clients = new Dictionary<string, IMcpClient>
        {
            ["test-server"] = mcpClientMock.Object
        };

        _mcpToolRegistryMock
            .Setup(x => x.GetAllRemoteClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(clients);

        var config = new McpMonitorConfig
        {
            ServerName = "test-server",
            PollInterval = TimeSpan.FromMilliseconds(100)
        };

        var monitorId = await _executor.StartMonitoringAsync(config, cts.Token).ConfigureAwait(true);

        monitorId.Should().NotBeNullOrEmpty();
        monitorId.Should().StartWith("monitor-");
    }

    [Fact]
    public async Task StartMonitoringAsync_NullConfig_ShouldThrowArgumentNullException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var act = () => _executor.StartMonitoringAsync(null!, cts.Token);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task GetActiveMonitorsAsync_NoMonitors_ShouldReturnEmptyList()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var monitors = await _executor.GetActiveMonitorsAsync(cts.Token).ConfigureAwait(true);

        monitors.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveMonitorsAsync_AfterStartingMonitor_ShouldReturnActiveMonitors()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var mcpClientMock = new Mock<IMcpClient>();
        mcpClientMock.SetupGet(x => x.IsConnected).Returns(true);
        mcpClientMock
            .Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpListToolsResult(true, Array.Empty<JoinCode.Abstractions.Tools.ToolInfo>()));
        mcpClientMock
            .Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpListResourcesResult(true, Array.Empty<McpResource>()));

        var clients = new Dictionary<string, IMcpClient>
        {
            ["test-server"] = mcpClientMock.Object
        };

        _mcpToolRegistryMock
            .Setup(x => x.GetAllRemoteClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(clients);

        var config = new McpMonitorConfig
        {
            ServerName = "test-server",
            PollInterval = TimeSpan.FromMilliseconds(100)
        };

        await _executor.StartMonitoringAsync(config, cts.Token).ConfigureAwait(true);
        var monitors = await _executor.GetActiveMonitorsAsync(cts.Token).ConfigureAwait(true);

        monitors.Should().NotBeEmpty();
        monitors[0].ServerName.Should().Be("test-server");
    }

    [Fact]
    public async Task StopMonitoringAsync_ShouldRemoveMonitor()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var mcpClientMock = new Mock<IMcpClient>();
        mcpClientMock.SetupGet(x => x.IsConnected).Returns(true);
        mcpClientMock
            .Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpListToolsResult(true, Array.Empty<JoinCode.Abstractions.Tools.ToolInfo>()));
        mcpClientMock
            .Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpListResourcesResult(true, Array.Empty<McpResource>()));

        var clients = new Dictionary<string, IMcpClient>
        {
            ["test-server"] = mcpClientMock.Object
        };

        _mcpToolRegistryMock
            .Setup(x => x.GetAllRemoteClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(clients);

        var config = new McpMonitorConfig
        {
            ServerName = "test-server",
            PollInterval = TimeSpan.FromMilliseconds(100)
        };

        var monitorId = await _executor.StartMonitoringAsync(config, cts.Token).ConfigureAwait(true);

        await _executor.StopMonitoringAsync(monitorId, cts.Token).ConfigureAwait(true);

        var monitors = await _executor.GetActiveMonitorsAsync(cts.Token).ConfigureAwait(true);
        monitors.Should().BeEmpty();
    }

    [Fact]
    public async Task StartMonitoringAsync_MonitorEventShouldFire()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var mcpClientMock = new Mock<IMcpClient>();
        mcpClientMock.SetupGet(x => x.IsConnected).Returns(true);
        mcpClientMock
            .Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpListToolsResult(true, new List<JoinCode.Abstractions.Tools.ToolInfo>
            {
                new() { Name = "tool-1", Description = "Test tool" }
            }));
        mcpClientMock
            .Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpListResourcesResult(true, Array.Empty<McpResource>()));

        var clients = new Dictionary<string, IMcpClient>
        {
            ["test-server"] = mcpClientMock.Object
        };

        _mcpToolRegistryMock
            .Setup(x => x.GetAllRemoteClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(clients);

        McpMonitorEventArgs? capturedArgs = null;
        using var eventSignal = new SemaphoreSlim(0, 1);
        _executor.MonitorEvent += (_, args) =>
        {
            capturedArgs = args;
            eventSignal.Release();
        };

        var config = new McpMonitorConfig
        {
            ServerName = "test-server",
            PollInterval = TimeSpan.FromMilliseconds(50)
        };

        await _executor.StartMonitoringAsync(config, cts.Token).ConfigureAwait(true);

        // 等待 MonitorEvent 触发，替代轮询
        await eventSignal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.EventType.Should().Be("tools_update");
        capturedArgs.ServerName.Should().Be("test-server");
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
