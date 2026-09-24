namespace Core.Tests.Agents.Coordinator;

/// <summary>
/// AgentDiscoveryService 单元测试 — 验证跨进程 agent 注册、心跳、发现
/// </summary>
public sealed class AgentDiscoveryServiceTests : IAsyncDisposable {
    private readonly string _tempRegistryPath;
    private readonly PhysicalFileSystem _fs;
    private readonly Mock<IClockService> _clockMock;
    private readonly AgentDiscoveryService _service;
    private static int _testCounter;
    private bool _disposed;

    public AgentDiscoveryServiceTests() {
        var tempDir = Path.Combine(Path.GetTempPath(), "jcc_agent_discovery_test", Interlocked.Increment(ref _testCounter).ToString());
        _tempRegistryPath = Path.Combine(tempDir, "registry.json");
        _fs = new PhysicalFileSystem();
        _clockMock = new Mock<IClockService>();
        _clockMock.Setup(c => c.GetUtcNow()).Returns(DateTime.UtcNow);
        _service = new AgentDiscoveryService(_fs, _tempRegistryPath, null, _clockMock.Object);
    }

    private static AgentRegistryInfo CreateAgentInfo(string agentId, string sessionId = "session1") => new() {
        AgentId = agentId,
        SessionId = sessionId,
        ProcessId = Environment.ProcessId,
        DisplayName = agentId,
        Role = "Worker",
    };

    [Fact]
    public async Task RegisterAsync_ThenDiscoverAsync_ReturnsRegisteredAgent() {
        var info = CreateAgentInfo("agent-001");

        await _service.RegisterAsync(info);
        var discovered = await _service.DiscoverAsync();

        discovered.Should().ContainSingle(a => a.AgentId == "agent-001");
    }

    [Fact]
    public async Task UnregisterAsync_RemovesAgentFromDiscovery() {
        var info = CreateAgentInfo("agent-002");
        await _service.RegisterAsync(info);

        await _service.UnregisterAsync("agent-002", "session1");
        var discovered = await _service.DiscoverAsync();

        discovered.Should().NotContain(a => a.AgentId == "agent-002");
    }

    [Fact]
    public async Task DiscoverAsync_FiltersOutStaleAgents() {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _clockMock.Setup(c => c.GetUtcNow()).Returns(baseTime);

        var info = CreateAgentInfo("agent-fresh");
        info.LastHeartbeat = baseTime;
        await _service.RegisterAsync(info);

        var staleInfo = CreateAgentInfo("agent-stale");
        staleInfo.LastHeartbeat = baseTime - TimeSpan.FromSeconds(60);
        await _service.RegisterAsync(staleInfo);

        var discovered = await _service.DiscoverAsync();

        discovered.Should().ContainSingle(a => a.AgentId == "agent-fresh");
        discovered.Should().NotContain(a => a.AgentId == "agent-stale");
    }

    [Fact]
    public async Task DiscoverBySessionAsync_FiltersBySession() {
        var info1 = CreateAgentInfo("agent-a", "session1");
        var info2 = CreateAgentInfo("agent-b", "session2");
        await _service.RegisterAsync(info1);
        await _service.RegisterAsync(info2);

        var session1Agents = await _service.DiscoverBySessionAsync("session1");

        session1Agents.Should().ContainSingle(a => a.AgentId == "agent-a");
        session1Agents.Should().NotContain(a => a.AgentId == "agent-b");
    }

    [Fact]
    public async Task HeartbeatAsync_UpdatesLastHeartbeat() {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _clockMock.Setup(c => c.GetUtcNow()).Returns(baseTime);

        var info = CreateAgentInfo("agent-hb");
        await _service.RegisterAsync(info);

        var laterTime = baseTime + TimeSpan.FromSeconds(5);
        _clockMock.Setup(c => c.GetUtcNow()).Returns(laterTime);
        await _service.HeartbeatAsync("agent-hb", "session1");

        var discovered = await _service.DiscoverAsync();
        var agent = discovered.Should().ContainSingle(a => a.AgentId == "agent-hb").Subject;
        agent.LastHeartbeat.Should().Be(laterTime);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;
        await _service.DisposeSafeAsync();
    }
}