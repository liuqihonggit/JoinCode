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
}
