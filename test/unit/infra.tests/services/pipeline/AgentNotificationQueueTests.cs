namespace Infrastructure.Pipeline;

public sealed class AgentNotificationQueueTests {
    [Fact]
    public async Task Enqueue_DequeueAll_ReturnsAllNotifications() {
        await using var queue = new AgentNotificationQueue();
        queue.Enqueue(null, "<task-notification>test1</task-notification>");
        queue.Enqueue(null, "<task-notification>test2</task-notification>");

        queue.HasPendingNotifications.Should().BeTrue();

        var results = queue.DequeueAll();
        results.Should().HaveCount(2);
        results[0].Xml.Should().Contain("test1");
        results[1].Xml.Should().Contain("test2");

        queue.HasPendingNotifications.Should().BeFalse();
    }

    [Fact]
    public async Task DequeueAll_WithAgentId_FiltersByTarget() {
        await using var queue = new AgentNotificationQueue();
        queue.Enqueue("agent-1", "notification-for-1");
        queue.Enqueue("agent-2", "notification-for-2");
        queue.Enqueue(null, "notification-for-main");

        var results = queue.DequeueAll("agent-1");
        results.Should().HaveCount(2);
        results.Should().Contain(n => n.Xml == "notification-for-1");
        results.Should().Contain(n => n.Xml == "notification-for-main");
        results.Should().NotContain(n => n.Xml == "notification-for-2");
    }

    [Fact]
    public async Task DequeueAll_EmptyQueue_ReturnsEmpty() {
        await using var queue = new AgentNotificationQueue();
        queue.HasPendingNotifications.Should().BeFalse();
        queue.DequeueAll().Should().BeEmpty();
    }

    [Fact]
    public async Task DequeueAll_DrainsQueue_SecondCallReturnsEmpty() {
        await using var queue = new AgentNotificationQueue();
        queue.Enqueue(null, "test");

        queue.DequeueAll().Should().HaveCount(1);
        queue.DequeueAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Enqueue_NullTargetAgentId_AcceptedByAll() {
        await using var queue = new AgentNotificationQueue();
        queue.Enqueue(null, "main-notification");

        var forMain = queue.DequeueAll(null);
        forMain.Should().HaveCount(1);

        queue.Enqueue(null, "another-main");
        var forAgent = queue.DequeueAll("any-agent");
        forAgent.Should().HaveCount(1);
    }

    [Fact]
    public async Task QueuedNotification_HasEnqueuedAt() {
        await using var queue = new AgentNotificationQueue();
        var before = DateTime.UtcNow.AddSeconds(-1);
        queue.Enqueue(null, "test");
        var after = DateTime.UtcNow.AddSeconds(1);

        var results = queue.DequeueAll();
        results[0].EnqueuedAt.Should().BeOnOrAfter(before);
        results[0].EnqueuedAt.Should().BeOnOrBefore(after);
    }
}