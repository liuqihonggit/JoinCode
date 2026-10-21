namespace JoinCode.Agents.Tests.Coordinator.Misc;

/// <summary>
/// 子代理消息路由循环守卫测试（TASK062 P2+P3）—— ReceiveAsync 漏传 ct 缺陷模式的防再犯守卫：
/// 路由循环必须把取消令牌传入邮箱底层（MailboxBase.ReadAllAsync(ct)），取消后循环在时限内收尾。
/// 回归形态：漏传 ct 时无消息场景 Channel 读永不退出，DisposeAsync 永挂（E2E 实测 Host.DisposeAsync
/// 挂死根因——dumpasync 链 AgentServiceImpl.DisposeAsync → BackgroundTaskActor → RouteWorker...Async）。
/// 单一职责：AgentMessageRouteLoop 把"ct 传递"收敛为类内一处构造契约，三个路由器消费消除散落复制。
/// </summary>
public sealed class AgentMessageRouteLoopTests
{
    /// <summary>构造 fake 邮箱（真实 InProcessMailbox—— ReadAllAsync(ct) 底层契约不 mock）。</summary>
    private static InProcessMailbox CreateBroker() => new(logger: null);

    /// <summary>守卫①：无消息场景 DisposeAsync 必须在时限内收尾（挂死回归即失败—— 接受取消传播到底层）。</summary>
    [Fact]
    public async Task ShutdownWithNoMessages_CompletesWithinDeadline()
    {
        var broker = CreateBroker();
        broker.RegisterAgent("agent-1", "session-1");
        await using var loop = new AgentMessageRouteLoop(broker, "agent-1", static (_, _) => Task.CompletedTask);
        loop.Start();

        await loop.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>守卫②：路由消息经 handler 派发（单一职责—— 循环只路由不分发语义）。</summary>
    [Fact]
    public async Task Start_RoutesMessagesToHandler()
    {
        var broker = CreateBroker();
        broker.RegisterAgent("agent-1", "session-1");
        await broker.WaitForCommandsDrainedAsync();
        var received = new List<CoordinatorMessage>();
        await using var loop = new AgentMessageRouteLoop(broker, "agent-1", (msg, _) => { received.Add(msg); return Task.CompletedTask; });
        loop.Start();

        await broker.SendAsync("agent-1", new CoordinatorMessage { MessageType = "test.ping", MessageId = "m-1", FromAgentId = "sender", ToAgentId = "agent-1", Content = "{}" }, CancellationToken.None);
        await WaitUntilAsync(() => received.Count >= 1, TimeSpan.FromSeconds(5));
        Assert.Equal("test.ping", received[0].MessageType);
    }

    /// <summary>守卫③：Dispose 后不再路由新消息（停止语义）。</summary>
    [Fact]
    public async Task AfterShutdown_NewMessagesNotRouted()
    {
        var broker = CreateBroker();
        broker.RegisterAgent("agent-1", "session-1");
        await broker.WaitForCommandsDrainedAsync();
        var received = new List<CoordinatorMessage>();
        await using var loop = new AgentMessageRouteLoop(broker, "agent-1", (msg, _) => { received.Add(msg); return Task.CompletedTask; });
        loop.Start();
        await loop.DisposeAsync();

        await broker.SendAsync("agent-1", new CoordinatorMessage { MessageType = "test.late", MessageId = "m-2", FromAgentId = "sender", ToAgentId = "agent-1", Content = "{}" }, CancellationToken.None);
        await Task.Delay(200);
        Assert.DoesNotContain(received, m => m.MessageType == "test.late");
    }

    /// <summary>未注册邮箱的 agentId — 空流立即完成（MailboxHub 未注册返回空流契约）。</summary>
    [Fact]
    public async Task Start_UnregisteredAgent_EmptyStreamCompletes()
    {
        var broker = CreateBroker();
        await using var loop = new AgentMessageRouteLoop(broker, "ghost", static (_, _) => Task.CompletedTask);
        loop.Start();

        await loop.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>轮询等待条件成立（守卫测试的确定性等待—— 通知语义非固定延时）。</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(20, cts.Token);
        }
    }
}
