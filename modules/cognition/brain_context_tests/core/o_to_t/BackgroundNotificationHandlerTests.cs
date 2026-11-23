namespace Core.Context;

/// <summary>
/// BackgroundNotificationHandler 单元测试 — 验证通知排空、格式包装、上下文注入与队列状态。
/// </summary>
public sealed class BackgroundNotificationHandlerTests {
    [Fact]
    public async Task DrainPendingNotificationTexts_ShellNotification_ReturnsRawXmlAndClearsQueue() {
        var queue = new AgentNotificationQueue();
        const string xml = "<task-notification><task-id>t-1</task-id></task-notification>";
        queue.Enqueue(null, xml);

        await using var handler = new BackgroundNotificationHandler(new Mock<IChatContextManager>().Object, queue);

        var texts = handler.DrainPendingNotificationTexts();

        texts.Should().ContainSingle().Which.Should().Be(xml);
        handler.HasPendingNotifications.Should().BeFalse();
    }

    [Fact]
    public async Task DrainPendingNotificationTexts_AgentNotification_WrapsWithCompletionHeader() {
        var queue = new AgentNotificationQueue();
        queue.Enqueue("agent-1", "<result>done</result>");

        await using var handler = new BackgroundNotificationHandler(new Mock<IChatContextManager>().Object, queue);

        var text = handler.DrainPendingNotificationTexts().Should().ContainSingle().Subject;

        text.Should().StartWith("A background agent completed a task:");
        text.Should().Contain("<result>done</result>");
    }

    [Fact]
    public async Task DrainPendingNotificationTexts_EmptyQueue_ReturnsEmpty() {
        await using var handler = new BackgroundNotificationHandler(new Mock<IChatContextManager>().Object, new AgentNotificationQueue());

        handler.DrainPendingNotificationTexts().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessPendingNotificationsAsync_AddsUserMessageWithTaskNotificationOrigin() {
        var queue = new AgentNotificationQueue();
        queue.Enqueue(null, "<task-notification><task-id>t-2</task-id></task-notification>");
        var contextManager = new Mock<IChatContextManager>();
        contextManager
            .Setup(c => c.AddUserMessageAsync(It.IsAny<string>(), It.IsAny<MessageOriginKind?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var handler = new BackgroundNotificationHandler(contextManager.Object, queue);

        var injected = await handler.ProcessPendingNotificationsAsync(CancellationToken.None);

        injected.Should().Be(1);
        contextManager.Verify(c => c.AddUserMessageAsync(
            It.Is<string>(text => text.Contains("t-2")),
            MessageOriginKind.TaskNotification,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
