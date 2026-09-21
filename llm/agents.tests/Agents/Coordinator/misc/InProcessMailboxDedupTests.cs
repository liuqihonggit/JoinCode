namespace Core.Tests.Agents.Coordinator;

/// <summary>
/// InProcessMailbox 消息去重单元测试 — 验证 MessageId 去重逻辑
/// </summary>
public sealed class InProcessMailboxDedupTests {
    private static AgentMsg CreateMessage(string messageId, string from = "sender", string to = "agent1") => new() {
        MessageId = messageId,
        FromAgentId = from,
        ToAgentId = to,
        MessageType = "text",
        Content = "hello",
    };

    [Fact]
    public async Task SendAsync_SameMessageId_SecondCallReturnsFalse() {
        await using var mailbox = new InProcessMailbox();
        mailbox.RegisterAgent("agent1");
        var msg = CreateMessage("msg-001");

        var first = await mailbox.SendAsync("agent1", msg);
        var second = await mailbox.SendAsync("agent1", msg);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_DifferentMessageId_BothDelivered() {
        await using var mailbox = new InProcessMailbox();
        mailbox.RegisterAgent("agent1");

        var first = await mailbox.SendAsync("agent1", CreateMessage("msg-001"));
        var second = await mailbox.SendAsync("agent1", CreateMessage("msg-002"));

        first.Should().BeTrue();
        second.Should().BeTrue();
    }

    [Fact]
    public async Task DeliverInboundAsync_SameMessageId_SecondCallSkipped() {
        await using var mailbox = new InProcessMailbox();
        mailbox.RegisterAgent("agent1");
        await WaitForRegistrationAsync(mailbox, "agent1");
        var msg = CreateMessage("msg-001");

        await mailbox.DeliverInboundAsync("agent1", msg);
        await mailbox.DeliverInboundAsync("agent1", msg);

        var messages = new List<AgentMsg>();
        await foreach (var m in mailbox.ReceiveAsync("agent1", CancellationToken.None)) {
            messages.Add(m);
            if (messages.Count >= 1) break;
        }

        messages.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAsync_DifferentAgent_SameMessageId_BothDelivered() {
        await using var mailbox = new InProcessMailbox();
        mailbox.RegisterAgent("agent1");
        mailbox.RegisterAgent("agent2");
        var msg = CreateMessage("msg-001");

        var first = await mailbox.SendAsync("agent1", msg);
        var second = await mailbox.SendAsync("agent2", msg);

        first.Should().BeTrue();
        second.Should().BeTrue();
    }

    [Fact]
    public async Task UnregisterAgent_AfterUnregister_SameMessageId_CanDeliverAgain() {
        await using var mailbox = new InProcessMailbox();
        mailbox.RegisterAgent("agent1");
        var msg = CreateMessage("msg-001");

        await mailbox.SendAsync("agent1", msg);
        mailbox.UnregisterAgent("agent1");

        mailbox.RegisterAgent("agent1");
        var result = await mailbox.SendAsync("agent1", msg);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_Duplicate_DoesNotPersistToMailbox() {
        var fileMailboxMock = new Mock<ITeammateMailboxService>();
        fileMailboxMock
            .Setup(m => m.SendAsync(It.IsAny<MailboxSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailboxMessage { MessageId = "x", FromAgentId = "a", ToAgentId = "b", MessageType = "t", Content = "c", SessionId = "s" });

        await using var mailbox = new InProcessMailbox(null, fileMailboxMock.Object);
        mailbox.RegisterAgent("agent1", "session1");
        await WaitForRegistrationAsync(mailbox, "agent1");
        var msg = CreateMessage("msg-001");

        await mailbox.SendAsync("agent1", msg);
        await mailbox.SendAsync("agent1", msg);

        fileMailboxMock.Verify(m => m.SendAsync(It.IsAny<MailboxSendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task WaitForRegistrationAsync(InProcessMailbox mailbox, string agentId) {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline) {
            if (mailbox.GetRegisteredAgents().Contains(agentId)) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Agent {agentId} not registered within 5s");
    }
}