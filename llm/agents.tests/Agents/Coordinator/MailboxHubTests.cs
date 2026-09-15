namespace Core.Tests.Agents.Coordinator;

public sealed class MailboxHubTests
{
    private readonly Mock<IMailbox> _inProcessMock = new();
    private readonly Mock<ITeammateMailboxService> _fileMailboxMock = new();

    private static AgentMsg CreateMessage(string from = "sender", string to = "agent1") => new()
    {
        FromAgentId = from,
        ToAgentId = to,
        MessageType = "text",
        Content = "hello",
    };

    private static MailboxMessage CreateMailboxMessage() => new()
    {
        MessageId = "msg1",
        FromAgentId = "sender",
        ToAgentId = "agent1",
        MessageType = "text",
        Content = "hello",
        SessionId = "session1",
    };

    [Fact]
    public async Task SendAsync_InProcess_DelegatesToInProcessMailbox()
    {
        _inProcessMock.Setup(m => m.SendAsync("agent1", It.IsAny<CoordinatorMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var hub = new MailboxHub(_inProcessMock.Object, _fileMailboxMock.Object);
        var message = CreateMessage();

        var result = await hub.SendAsync("agent1", message, MailboxKind.InProcess);

        result.Should().BeTrue();
        _inProcessMock.Verify(m => m.SendAsync("agent1", message, It.IsAny<CancellationToken>()), Times.Once);
        _fileMailboxMock.Verify(m => m.SendAsync(It.IsAny<MailboxSendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_File_DelegatesToFileMailbox()
    {
        _inProcessMock.Setup(m => m.GetSessionId("agent1")).Returns("session1");
        _fileMailboxMock.Setup(m => m.SendAsync(It.IsAny<MailboxSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMailboxMessage());

        var hub = new MailboxHub(_inProcessMock.Object, _fileMailboxMock.Object);
        var message = CreateMessage();

        var result = await hub.SendAsync("agent1", message, MailboxKind.File);

        result.Should().BeTrue();
        _fileMailboxMock.Verify(m => m.SendAsync(It.IsAny<MailboxSendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_File_NoFileMailbox_ReturnsFalse()
    {
        var hub = new MailboxHub(_inProcessMock.Object, null);
        var message = CreateMessage();

        var result = await hub.SendAsync("agent1", message, MailboxKind.File);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_File_NoSessionId_ReturnsFalse()
    {
        _inProcessMock.Setup(m => m.GetSessionId("agent1")).Returns((string?)null);

        var hub = new MailboxHub(_inProcessMock.Object, _fileMailboxMock.Object);
        var message = CreateMessage();

        var result = await hub.SendAsync("agent1", message, MailboxKind.File);

        result.Should().BeFalse();
        _fileMailboxMock.Verify(m => m.SendAsync(It.IsAny<MailboxSendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BroadcastAsync_InProcess_DelegatesToInProcessMailbox()
    {
        var message = CreateMessage();
        _inProcessMock.Setup(m => m.BroadcastAsync(message, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hub = new MailboxHub(_inProcessMock.Object, _fileMailboxMock.Object);

        await hub.BroadcastAsync(message, MailboxKind.InProcess);

        _inProcessMock.Verify(m => m.BroadcastAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RegisterAgent_DelegatesToInProcessMailbox()
    {
        var hub = new MailboxHub(_inProcessMock.Object);

        hub.RegisterAgent("agent1", "session1");

        _inProcessMock.Verify(m => m.RegisterAgent("agent1", "session1"), Times.Once);
    }

    [Fact]
    public void UnregisterAgent_DelegatesToInProcessMailbox()
    {
        var hub = new MailboxHub(_inProcessMock.Object);

        hub.UnregisterAgent("agent1");

        _inProcessMock.Verify(m => m.UnregisterAgent("agent1"), Times.Once);
    }

    [Fact]
    public void Constructor_NullInProcess_Throws()
    {
        var act = () => new MailboxHub(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterChannel_InProcess_Throws()
    {
        var hub = new MailboxHub(_inProcessMock.Object);
        var act = () => hub.RegisterChannel(MailboxKind.InProcess, new InProcessMailbox());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterChannel_File_Throws()
    {
        var hub = new MailboxHub(_inProcessMock.Object);
        var act = () => hub.RegisterChannel(MailboxKind.File, new InProcessMailbox());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RegisterChannel_NamedPipe_ThenSendAsync_RoutesToExtraChannel()
    {
        await using var namedPipeMailbox = new InProcessMailbox();
        await namedPipeMailbox.RegisterAgentAsync("agent1");
        var hub = new MailboxHub(_inProcessMock.Object);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);

        var message = CreateMessage();
        var result = await hub.SendAsync("agent1", message, MailboxKind.NamedPipe);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_UnregisteredNamedPipe_ReturnsFalse()
    {
        var hub = new MailboxHub(_inProcessMock.Object);
        var message = CreateMessage();

        var result = await hub.SendAsync("agent1", message, MailboxKind.NamedPipe);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsChannelAvailable_ReturnsCorrectAvailability()
    {
        var hub = new MailboxHub(_inProcessMock.Object, _fileMailboxMock.Object);

        hub.IsChannelAvailable(MailboxKind.InProcess).Should().BeTrue();
        hub.IsChannelAvailable(MailboxKind.File).Should().BeTrue();
        hub.IsChannelAvailable(MailboxKind.NamedPipe).Should().BeFalse();
        hub.IsChannelAvailable(MailboxKind.Network).Should().BeFalse();

        await using var namedPipeMailbox = new InProcessMailbox();
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);
        hub.IsChannelAvailable(MailboxKind.NamedPipe).Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_AutoRoute_DefaultsToInProcess()
    {
        _inProcessMock.Setup(m => m.SendAsync("agent1", It.IsAny<CoordinatorMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var hub = new MailboxHub(_inProcessMock.Object);
        var message = CreateMessage();

        var result = await hub.SendAsync("agent1", message);

        result.Should().BeTrue();
        _inProcessMock.Verify(m => m.SendAsync("agent1", message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_AutoRoute_RoutesToRegisteredKind()
    {
        await using var namedPipeMailbox = new InProcessMailbox();
        await namedPipeMailbox.RegisterAgentAsync("agent1");
        var hub = new MailboxHub(_inProcessMock.Object);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);
        await hub.RegisterAgentAsync("agent1", MailboxKind.NamedPipe);

        var message = CreateMessage();
        var result = await hub.SendAsync("agent1", message);

        result.Should().BeTrue();
        _inProcessMock.Verify(m => m.SendAsync("agent1", It.IsAny<CoordinatorMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BroadcastAsync_CrossChannel_BroadcastsToAllChannels()
    {
        _inProcessMock.Setup(m => m.BroadcastAsync(It.IsAny<CoordinatorMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _inProcessMock.Setup(m => m.GetRegisteredAgents()).Returns([]);

        await using var namedPipeMailbox = new InProcessMailbox();
        await namedPipeMailbox.RegisterAgentAsync("agent1");
        await using var networkMailbox = new InProcessMailbox();
        await networkMailbox.RegisterAgentAsync("agent2");

        var hub = new MailboxHub(_inProcessMock.Object);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);
        hub.RegisterChannel(MailboxKind.Network, networkMailbox);

        var message = CreateMessage();
        await hub.BroadcastAsync(message);

        _inProcessMock.Verify(m => m.BroadcastAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAgentAsync_WithKind_StoresChannelPreference()
    {
        await using var namedPipeMailbox = new InProcessMailbox();
        var hub = new MailboxHub(_inProcessMock.Object);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);

        await hub.RegisterAgentAsync("agent1", MailboxKind.NamedPipe);

        hub.GetAgentChannel("agent1").Should().Be(MailboxKind.NamedPipe);
    }

    [Fact]
    public async Task RegisterAgentAsync_InProcess_StoresChannelPreference()
    {
        var hub = new MailboxHub(_inProcessMock.Object);

        await hub.RegisterAgentAsync("agent1", MailboxKind.InProcess, "session1");

        hub.GetAgentChannel("agent1").Should().Be(MailboxKind.InProcess);
        _inProcessMock.Verify(m => m.RegisterAgent("agent1", "session1"), Times.Once);
    }

    [Fact]
    public async Task UnregisterAgentAsync_RemovesChannelPreference()
    {
        var hub = new MailboxHub(_inProcessMock.Object);

        await hub.RegisterAgentAsync("agent1", MailboxKind.InProcess);
        hub.GetAgentChannel("agent1").Should().Be(MailboxKind.InProcess);

        await hub.UnregisterAgentAsync("agent1");
        hub.GetAgentChannel("agent1").Should().Be(MailboxKind.InProcess);
    }

    [Fact]
    public async Task ReceiveAsync_FromNamedPipeChannel_ReturnsMessages()
    {
        await using var namedPipeMailbox = new InProcessMailbox();
        await namedPipeMailbox.RegisterAgentAsync("agent1");
        var hub = new MailboxHub(_inProcessMock.Object);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);
        await hub.RegisterAgentAsync("agent1", MailboxKind.NamedPipe);

        var message = CreateMessage(to: "agent1");
        await namedPipeMailbox.TellAsync("agent1", message);
        await Task.Delay(100);

        var received = new List<CoordinatorMessage>();
        await foreach (var msg in hub.ReceiveAsync("agent1", CancellationToken.None))
        {
            received.Add(msg);
            break;
        }

        received.Should().HaveCount(1);
        received[0].Content.Should().Be("hello");
    }
}
