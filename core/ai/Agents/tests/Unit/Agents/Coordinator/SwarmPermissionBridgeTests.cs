
namespace Sync.Tests.Agents.Coordinator;

public class SwarmPermissionBridgeTests
{
    private readonly Mock<IAgentMessageBroker> _messageBrokerMock;
    private readonly Mock<IAgentPermissionManager> _permissionManagerMock;
    private readonly SwarmPermissionBridge _bridge;

    public SwarmPermissionBridgeTests()
    {
        _messageBrokerMock = new Mock<IAgentMessageBroker>();
        _permissionManagerMock = new Mock<IAgentPermissionManager>();
        _bridge = new SwarmPermissionBridge(
            _messageBrokerMock.Object,
            _permissionManagerMock.Object,
            NullLogger<SwarmPermissionBridge>.Instance);
    }

    [Fact]
    public async Task SyncPermissionsAsync_ShouldSyncPermissionsAndFireEvent()
    {
        _permissionManagerMock
            .Setup(x => x.AddRuleAsync(It.IsAny<AgentPermissionRule>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _messageBrokerMock
            .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<AgentMsg>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        PermissionSyncEventArgs? capturedArgs = null;
        _bridge.PermissionChanged += (_, args) => capturedArgs = args;

        var request = new PermissionSyncRequest
        {
            AgentId = "agent-1",
            CoordinatorId = "coordinator-1",
            Mode = PermissionMode.Auto,
            AllowedTools = new List<string> { "tool-1", "tool-2" },
            DeniedTools = new List<string> { "tool-3" }
        };

        await _bridge.SyncPermissionsAsync("agent-1", request).ConfigureAwait(true);

        _permissionManagerMock.Verify(
            x => x.AddRuleAsync(It.IsAny<AgentPermissionRule>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _messageBrokerMock.Verify(
            x => x.SendMessageAsync("agent-1", It.IsAny<AgentMsg>(), It.IsAny<CancellationToken>()),
            Times.Once);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.AgentId.Should().Be("agent-1");
        capturedArgs.ChangeType.Should().Be("sync");
    }

    [Fact]
    public async Task SyncPermissionsAsync_ShouldStorePermissionState()
    {
        _permissionManagerMock
            .Setup(x => x.AddRuleAsync(It.IsAny<AgentPermissionRule>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _messageBrokerMock
            .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<AgentMsg>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new PermissionSyncRequest
        {
            AgentId = "agent-2",
            CoordinatorId = "coordinator-1",
            Mode = PermissionMode.Ask,
            AllowedTools = new List<string> { "read_tool" }
        };

        await _bridge.SyncPermissionsAsync("agent-2", request).ConfigureAwait(true);

        var state = await _bridge.GetPermissionStateAsync("agent-2").ConfigureAwait(true);

        state.AgentId.Should().Be("agent-2");
        state.Mode.Should().Be(PermissionMode.Ask);
        state.AllowedTools.Should().Contain("read_tool");
    }

    [Fact]
    public async Task GetPermissionStateAsync_NoSyncedState_ShouldFallbackToPermissionManager()
    {
        var rule = new AgentPermissionRule
        {
            AgentPattern = "agent-3",
            Mode = PermissionMode.Plan,
            AllowedTools = new List<string> { "plan_tool" }
        };

        _permissionManagerMock
            .Setup(x => x.GetRuleForAgentAsync("agent-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);

        var state = await _bridge.GetPermissionStateAsync("agent-3").ConfigureAwait(true);

        state.AgentId.Should().Be("agent-3");
        state.Mode.Should().Be(PermissionMode.Plan);
        state.AllowedTools.Should().Contain("plan_tool");
    }

    [Fact]
    public async Task GetPermissionStateAsync_NoRuleFound_ShouldReturnAutoMode()
    {
        _permissionManagerMock
            .Setup(x => x.GetRuleForAgentAsync("agent-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentPermissionRule?)null);

        var state = await _bridge.GetPermissionStateAsync("agent-unknown").ConfigureAwait(true);

        state.AgentId.Should().Be("agent-unknown");
        state.Mode.Should().Be(PermissionMode.Auto);
    }

    [Fact]
    public async Task RevokePermissionsAsync_ShouldRemoveRuleAndFireEvent()
    {
        _permissionManagerMock
            .Setup(x => x.RemoveRuleAsync("agent-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        PermissionSyncEventArgs? capturedArgs = null;
        _bridge.PermissionChanged += (_, args) => capturedArgs = args;

        await _bridge.RevokePermissionsAsync("agent-4").ConfigureAwait(true);

        _permissionManagerMock.Verify(
            x => x.RemoveRuleAsync("agent-4", It.IsAny<CancellationToken>()),
            Times.Once);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.AgentId.Should().Be("agent-4");
        capturedArgs.ChangeType.Should().Be("revoke");
    }

    [Fact]
    public async Task RevokePermissionsAsync_ShouldRemoveCachedState()
    {
        _permissionManagerMock
            .Setup(x => x.AddRuleAsync(It.IsAny<AgentPermissionRule>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _permissionManagerMock
            .Setup(x => x.RemoveRuleAsync("agent-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _messageBrokerMock
            .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<AgentMsg>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new PermissionSyncRequest
        {
            AgentId = "agent-5",
            CoordinatorId = "coordinator-1",
            Mode = PermissionMode.Auto
        };

        await _bridge.SyncPermissionsAsync("agent-5", request).ConfigureAwait(true);
        await _bridge.RevokePermissionsAsync("agent-5").ConfigureAwait(true);

        _permissionManagerMock
            .Setup(x => x.GetRuleForAgentAsync("agent-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentPermissionRule?)null);

        var state = await _bridge.GetPermissionStateAsync("agent-5").ConfigureAwait(true);
        state.Mode.Should().Be(PermissionMode.Auto);
        state.AllowedTools.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullMessageBroker_ShouldThrowArgumentNullException()
    {
        var act = () => new SwarmPermissionBridge(null!, _permissionManagerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("messageBroker");
    }

    [Fact]
    public void Constructor_NullPermissionManager_ShouldThrowArgumentNullException()
    {
        var act = () => new SwarmPermissionBridge(_messageBrokerMock.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("permissionManager");
    }
}
