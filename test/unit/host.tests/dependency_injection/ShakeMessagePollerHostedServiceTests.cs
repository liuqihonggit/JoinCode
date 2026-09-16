using Core.DependencyInjection;

namespace Host.Tests.DependencyInjection;

/// <summary>
/// ShakeMessagePollerHostedService 单元测试 — 验证跨进程 shake 消息轮询行为
/// </summary>
public sealed class ShakeMessagePollerHostedServiceTests
{
    private static Mock<IWindowShakeCoordinator> CreateCoordinator(bool enabled = true, bool canShake = true)
    {
        var mock = new Mock<IWindowShakeCoordinator>();
        mock.SetupGet(x => x.IsShakeEnabled).Returns(enabled);
        mock.Setup(x => x.TryAcquireShakeSlot()).Returns(canShake);
        return mock;
    }

    private static CoordinatorMessage CreateShakeMessage(string messageType = "shake")
        => new()
        {
            MessageId = Guid.NewGuid().ToString("N"),
            FromAgentId = "bot-123",
            ToAgentId = "all-agents",
            MessageType = messageType,
            Content = "MACHINE:123:reason",
            SessionId = "shake-broadcast",
            Timestamp = DateTime.UtcNow,
            IsRead = false
        };

    [Fact]
    public async Task StartAsync_MailboxServiceNull_DoesNotStartPolling()
    {
        var coordinator = CreateCoordinator();
        var service = new ShakeMessagePollerHostedService(coordinator.Object);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        coordinator.Verify(x => x.TryAcquireShakeSlot(), Times.Never);
    }

    [Fact]
    public async Task PollOnceAsync_ReceivesShakeMessage_ShakesWindow()
    {
        var mailboxMock = new Mock<ITeammateMailboxService>();
        var shakeMsg = CreateShakeMessage();
        mailboxMock.Setup(x => x.ReadUnreadAsync("all-agents", "shake-broadcast", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoordinatorMessage> { shakeMsg });

        var shakeServiceMock = new Mock<IWindowShakeService>();
        shakeServiceMock.Setup(x => x.ShakeWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShakeResult("TestWindow", "0x7B", "test"));

        var coordinator = CreateCoordinator(enabled: true, canShake: true);
        var service = new ShakeMessagePollerHostedService(
            coordinator.Object, mailboxMock.Object, shakeServiceMock.Object);

        await service.PollOnceAsync(CancellationToken.None);

        shakeServiceMock.Verify(x => x.ShakeWindowAsync(It.IsAny<CancellationToken>()), Times.Once);
        mailboxMock.Verify(x => x.MarkAsReadAsync("all-agents", "shake-broadcast",
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PollOnceAsync_NoShakeMessages_DoesNotShake()
    {
        var mailboxMock = new Mock<ITeammateMailboxService>();
        mailboxMock.Setup(x => x.ReadUnreadAsync("all-agents", "shake-broadcast", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoordinatorMessage>());

        var shakeServiceMock = new Mock<IWindowShakeService>();
        var coordinator = CreateCoordinator();
        var service = new ShakeMessagePollerHostedService(
            coordinator.Object, mailboxMock.Object, shakeServiceMock.Object);

        await service.PollOnceAsync(CancellationToken.None);

        shakeServiceMock.Verify(x => x.ShakeWindowAsync(It.IsAny<CancellationToken>()), Times.Never);
        mailboxMock.Verify(x => x.MarkAsReadAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PollOnceAsync_NonShakeMessages_DoesNotShake()
    {
        var mailboxMock = new Mock<ITeammateMailboxService>();
        mailboxMock.Setup(x => x.ReadUnreadAsync("all-agents", "shake-broadcast", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoordinatorMessage> { CreateShakeMessage("text"), CreateShakeMessage("status") });

        var shakeServiceMock = new Mock<IWindowShakeService>();
        var coordinator = CreateCoordinator();
        var service = new ShakeMessagePollerHostedService(
            coordinator.Object, mailboxMock.Object, shakeServiceMock.Object);

        await service.PollOnceAsync(CancellationToken.None);

        shakeServiceMock.Verify(x => x.ShakeWindowAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PollOnceAsync_CoordinatorDisabled_DoesNotShake()
    {
        var mailboxMock = new Mock<ITeammateMailboxService>();
        mailboxMock.Setup(x => x.ReadUnreadAsync("all-agents", "shake-broadcast", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoordinatorMessage> { CreateShakeMessage() });

        var shakeServiceMock = new Mock<IWindowShakeService>();
        var coordinator = CreateCoordinator(enabled: false);
        var service = new ShakeMessagePollerHostedService(
            coordinator.Object, mailboxMock.Object, shakeServiceMock.Object);

        await service.PollOnceAsync(CancellationToken.None);

        shakeServiceMock.Verify(x => x.ShakeWindowAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PollOnceAsync_ShakeServiceNull_MarksAsReadWithoutShaking()
    {
        var mailboxMock = new Mock<ITeammateMailboxService>();
        mailboxMock.Setup(x => x.ReadUnreadAsync("all-agents", "shake-broadcast", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoordinatorMessage> { CreateShakeMessage() });

        var coordinator = CreateCoordinator();
        var service = new ShakeMessagePollerHostedService(
            coordinator.Object, mailboxMock.Object, shakeService: null);

        await service.PollOnceAsync(CancellationToken.None);

        mailboxMock.Verify(x => x.MarkAsReadAsync("all-agents", "shake-broadcast",
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
