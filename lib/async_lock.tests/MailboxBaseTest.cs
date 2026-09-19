namespace Core.Utils;

/// <summary>
/// MailboxBase 单元测试 — 验证双工、背压、水位线、tell 异步、注册/注销、广播。
/// </summary>
public class MailboxBaseTest {
    [Fact]
    public async Task RegisterAndTell_MessageDeliveredToAgent() {
        await using var mailbox = new TestMailbox();
        await mailbox.RegisterAgentAsync("agent-1");
        await WaitForRegistrationAsync(mailbox, "agent-1");
        await mailbox.TellAsync("agent-1", "hello");

        var msg = await mailbox.ReceiveAsync("agent-1").FirstAsync();
        msg.Should().Be("hello");
    }

    [Fact]
    public async Task TellAsync_DoesNotBlock_FireAndForget() {
        await using var mailbox = new TestMailbox(
            agentBp: new ActorBackpressure(Capacity: 2, FullMode: BoundedChannelFullMode.Wait, SendTimeout: TimeSpan.FromSeconds(5)));
        await mailbox.RegisterAgentAsync("agent-1");
        await WaitForRegistrationAsync(mailbox, "agent-1");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await mailbox.TellAsync("agent-1", "msg-1");
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "tell 应为 fire-and-forget，不阻塞");
    }

    [Fact]
    public async Task Broadcast_AllAgentsReceiveMessage() {
        await using var mailbox = new TestMailbox();
        await mailbox.RegisterAgentAsync("agent-1");
        await mailbox.RegisterAgentAsync("agent-2");
        await mailbox.RegisterAgentAsync("agent-3");
        await WaitForRegistrationAsync(mailbox, "agent-1");
        await WaitForRegistrationAsync(mailbox, "agent-2");
        await WaitForRegistrationAsync(mailbox, "agent-3");

        await mailbox.TellBroadcastAsync("broadcast-msg", excludeAgentId: "agent-1");

        var msg2 = await mailbox.ReceiveAsync("agent-2").FirstAsync();
        var msg3 = await mailbox.ReceiveAsync("agent-3").FirstAsync();
        msg2.Should().Be("broadcast-msg");
        msg3.Should().Be("broadcast-msg");
    }

    [Fact]
    public async Task Broadcast_ExcludesSender() {
        await using var mailbox = new TestMailbox();
        await mailbox.RegisterAgentAsync("sender");
        await mailbox.RegisterAgentAsync("receiver");
        await WaitForRegistrationAsync(mailbox, "sender");
        await WaitForRegistrationAsync(mailbox, "receiver");

        await mailbox.TellBroadcastAsync("msg", excludeAgentId: "sender");

        var received = await mailbox.ReceiveAsync("receiver").FirstAsync();
        received.Should().Be("msg");

        mailbox.GetAgentMessageCount("sender").Should().Be(0, "sender 应被排除");
    }

    [Fact]
    public async Task UnregisterAgent_ChannelCompletes() {
        await using var mailbox = new TestMailbox();
        await mailbox.RegisterAgentAsync("agent-1");
        await mailbox.UnregisterAgentAsync("agent-1");

        await WaitUntilAsync(() => !mailbox.GetRegisteredAgents().Contains("agent-1"), TimeSpan.FromSeconds(2));
        mailbox.GetRegisteredAgents().Should().NotContain("agent-1");
    }

    [Fact]
    public async Task Watermark_TriggersEvent_WhenHighWatermarkReached() {
        var agentBp = new ActorBackpressure(
            Capacity: 4,
            FullMode: BoundedChannelFullMode.Wait,
            HighWatermark: 3,
            CriticalWatermark: 4,
            SendTimeout: TimeSpan.FromSeconds(5));

        await using var mailbox = new TestMailbox(agentBp: agentBp);
        await mailbox.RegisterAgentAsync("agent-1");

        var watermarkEvents = new List<MailboxEvt<string>>();
        var cts = new CancellationTokenSource();
        var consumeTask = Task.Run(async () => {
            await foreach (var evt in mailbox.OutputAsync(cts.Token)) {
                if (evt is WatermarkReachedEvt<string> w)
                    watermarkEvents.Add(w);
            }
        }, cts.Token);

        for (var i = 0; i < 4; i++)
            await mailbox.TellAsync("agent-1", $"msg-{i}");

        await WaitUntilAsync(() => watermarkEvents.Count > 0, TimeSpan.FromSeconds(3));

        cts.Cancel();
        await Task.WhenAny(consumeTask, Task.Delay(1000));

        watermarkEvents.Should().NotBeEmpty("达到高水位线应触发事件");
        watermarkEvents.Should().AllSatisfy(evt =>
            ((WatermarkReachedEvt<string>)evt).AgentId.Should().Be("agent-1"));
    }

    [Fact]
    public async Task GetRegisteredAgents_ReturnsAllRegistered() {
        await using var mailbox = new TestMailbox();
        await mailbox.RegisterAgentAsync("a");
        await mailbox.RegisterAgentAsync("b");
        await mailbox.RegisterAgentAsync("c");
        await WaitForRegistrationAsync(mailbox, "a");
        await WaitForRegistrationAsync(mailbox, "b");
        await WaitForRegistrationAsync(mailbox, "c");

        mailbox.GetRegisteredAgents().Should().Contain(new[] { "a", "b", "c" });
    }

    [Fact]
    public async Task GetSessionId_ReturnsRegisteredSession() {
        await using var mailbox = new TestMailbox();
        await mailbox.RegisterAgentAsync("agent-1", "session-123");
        await WaitForRegistrationAsync(mailbox, "agent-1");

        mailbox.GetSessionId("agent-1").Should().Be("session-123");
        mailbox.GetSessionId("unknown").Should().BeNull();
    }

    [Fact]
    public async Task ReceiveAsync_UnregisteredAgent_ReturnsEmpty() {
        await using var mailbox = new TestMailbox();
        var count = await mailbox.ReceiveAsync("nonexistent").CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task IsAgentHighWatermark_TrueWhenAboveThreshold() {
        var agentBp = new ActorBackpressure(Capacity: 10, HighWatermark: 8, CriticalWatermark: 9);
        await using var mailbox = new TestMailbox(agentBp: agentBp);
        await mailbox.RegisterAgentAsync("agent-1");

        for (var i = 0; i < 9; i++)
            await mailbox.TellAsync("agent-1", $"msg-{i}");

        await WaitUntilAsync(() => mailbox.GetAgentMessageCount("agent-1") >= 9, TimeSpan.FromSeconds(2));
        mailbox.IsAgentHighWatermark("agent-1").Should().BeTrue();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout) {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) {
            if (predicate()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Condition not met within {timeout.TotalSeconds}s");
    }

    private static async Task WaitForRegistrationAsync(TestMailbox mailbox, string agentId) {
        await WaitUntilAsync(() => mailbox.GetRegisteredAgents().Contains(agentId), TimeSpan.FromSeconds(5));
    }
}

/// <summary>测试用邮箱 — string 消息类型，默认实现本地投递。</summary>
internal sealed class TestMailbox : MailboxBase<string> {
    public TestMailbox(ActorBackpressure? cmdBp = null, ActorBackpressure? agentBp = null)
        : base(cmdBp, agentBp, 64) { }
}