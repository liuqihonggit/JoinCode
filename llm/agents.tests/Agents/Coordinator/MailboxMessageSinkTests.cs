namespace Core.Tests.Agents.Coordinator;

/// <summary>
/// MailboxMessageSink 单元测试 — 验证跨进程消息投递到内存 Channel
/// </summary>
public sealed class MailboxMessageSinkTests
{
    [Fact]
    public async Task DeliverAsync_CallsDeliverInboundAsync_MessageReachesChannel()
    {
        var mailbox = new InProcessMailbox();
        mailbox.RegisterAgent("agent1");
        await Task.Delay(100);
        var sink = new MailboxMessageSink(mailbox);
        var msg = new AgentMsg { FromAgentId = "sender", ToAgentId = "agent1", MessageType = "text", Content = "hello" };

        await sink.DeliverAsync("agent1", msg);

        var received = new List<AgentMsg>();
        await foreach (var m in mailbox.ReceiveAsync("agent1", CancellationToken.None))
        {
            received.Add(m);
            break;
        }
        received.Should().HaveCount(1);
        received[0].Content.Should().Be("hello");
    }

    [Fact]
    public void Constructor_NonInProcessMailbox_Throws()
    {
        var mockMailbox = new Mock<IMailbox>();

        var act = () => new MailboxMessageSink(mockMailbox.Object);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DeliverAsync_ExceptionInMailbox_DoesNotPropagate()
    {
        var mailbox = new InProcessMailbox();
        var sink = new MailboxMessageSink(mailbox);
        var msg = new AgentMsg { FromAgentId = "sender", ToAgentId = "agent1", MessageType = "text", Content = "hello" };

        var act = () => sink.DeliverAsync("unregistered", msg);

        await act.Should().NotThrowAsync();
    }
}
