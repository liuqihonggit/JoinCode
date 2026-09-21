namespace Core.Utils;

/// <summary>
/// NetworkMailbox 单元测试 — 用 MockPlatformBotAdapter 验证邮箱与平台适配器的交互。
/// </summary>
public class NetworkMailboxTest {
    /// <summary>验证启动适配器并开始接收循环</summary>
    [Fact]
    public async Task StartAsync_应启动适配器并开始接收循环() {
        var adapter = new MockPlatformBotAdapter("test-platform");
        await using var mailbox = new NetworkMailbox(adapter);

        await mailbox.StartAsync();

        adapter.IsConnected.Should().BeTrue("适配器应已启动");
        mailbox.IsConnected.Should().BeTrue("邮箱应已连接");
        mailbox.PlatformName.Should().Be("test-platform");
    }

    /// <summary>验证本地投递并调用适配器发送</summary>
    [Fact]
    public async Task TellAsync_应本地投递且调用适配器发送() {
        var adapter = new MockPlatformBotAdapter("test");
        await using var mailbox = new NetworkMailbox(adapter);
        await mailbox.StartAsync();

        await mailbox.RegisterAgentAsync("agent-1", "session-1");
        await WaitForRegistrationAsync(mailbox, "agent-1");
        var msg = new CoordinatorMessage { FromAgentId = "sender", ToAgentId = "agent-1", MessageType = "text", Content = "hello" };

        await mailbox.TellAsync("agent-1", msg);
        await Task.Delay(100);

        adapter.SentMessages.Should().ContainSingle(m => m.targetId == "agent-1" && m.text == "hello");
    }

    /// <summary>验证广播消息对所有 Agent 发送</summary>
    [Fact]
    public async Task TellBroadcastAsync_应对所有Agent发送() {
        var adapter = new MockPlatformBotAdapter("test");
        await using var mailbox = new NetworkMailbox(adapter);
        await mailbox.StartAsync();

        await mailbox.RegisterAgentAsync("agent-1", "s1");
        await mailbox.RegisterAgentAsync("agent-2", "s1");
        await WaitForRegistrationAsync(mailbox, "agent-1");
        await WaitForRegistrationAsync(mailbox, "agent-2");
        var msg = new CoordinatorMessage { FromAgentId = "sender", ToAgentId = "all", MessageType = "text", Content = "broadcast" };

        await mailbox.TellBroadcastAsync(msg, excludeAgentId: null);
        await Task.Delay(100);

        adapter.SentMessages.Should().HaveCount(2, "应向两个Agent各发送一次");
    }

    /// <summary>验证广播消息排除指定 Agent</summary>
    [Fact]
    public async Task TellBroadcastAsync_应排除指定Agent() {
        var adapter = new MockPlatformBotAdapter("test");
        await using var mailbox = new NetworkMailbox(adapter);
        await mailbox.StartAsync();

        await mailbox.RegisterAgentAsync("agent-1", "s1");
        await mailbox.RegisterAgentAsync("agent-2", "s1");
        await WaitForRegistrationAsync(mailbox, "agent-1");
        await WaitForRegistrationAsync(mailbox, "agent-2");
        var msg = new CoordinatorMessage { FromAgentId = "sender", ToAgentId = "all", MessageType = "text", Content = "broadcast" };

        await mailbox.TellBroadcastAsync(msg, excludeAgentId: "agent-1");
        await Task.Delay(100);

        adapter.SentMessages.Should().ContainSingle(m => m.targetId == "agent-2");
    }

    /// <summary>验证适配器收到消息后投递到对应 Agent</summary>
    [Fact]
    public async Task 适配器收到消息_应投递到对应Agent() {
        var adapter = new MockPlatformBotAdapter("test");
        await using var mailbox = new NetworkMailbox(adapter);
        await mailbox.StartAsync();

        await mailbox.RegisterAgentAsync("agent-1", "s1");
        await WaitForRegistrationAsync(mailbox, "agent-1");

        adapter.Deliver(new PlatformMessage("remote-user", "agent-1", "from-remote", DateTimeOffset.UtcNow));

        var received = await mailbox.ReceiveAsync("agent-1").FirstOrDefaultAsync();
        received.Should().NotBeNull();
        received!.Content.Should().Be("from-remote");
        received.FromAgentId.Should().Be("remote-user");
    }

    /// <summary>
    /// 等待 Agent 注册完成 — RegisterAgentAsync 是 Tell 模式(只入队不等 Consumer 处理),
    /// 紧接的 ReceiveAsync 在 agent 未注册时返回空流导致 null。轮询 GetRegisteredAgents 确认注册完成。
    /// 与 MailboxBaseTest.WaitForRegistrationAsync 保持同一模式。
    /// </summary>
    private static async Task WaitForRegistrationAsync(NetworkMailbox mailbox, string agentId, TimeSpan? timeout = null) {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline && !mailbox.GetRegisteredAgents().Contains(agentId))
            await Task.Delay(10);
    }

    /// <summary>验证异步释放适配器</summary>
    [Fact]
    public async Task DisposeAsync_应释放适配器() {
        var adapter = new MockPlatformBotAdapter("test");
        var mailbox = new NetworkMailbox(adapter);
        await mailbox.StartAsync();

        await mailbox.DisposeAsync();

        adapter.IsDisposed.Should().BeTrue("适配器应被释放");
    }
}

/// <summary>
/// Mock 平台适配器 — 记录发送消息，支持手动投递接收消息。
/// </summary>
internal sealed class MockPlatformBotAdapter : IPlatformBotAdapter {
    private readonly Channel<PlatformMessage> _receiveChannel = Channel.CreateUnbounded<PlatformMessage>();
    private int _started;
    private int _disposed;

    /// <summary>获取平台名称</summary>
    public string PlatformName { get; }
    /// <summary>获取已发送消息列表</summary>
    public List<(string targetId, string text)> SentMessages { get; } = new();
    /// <summary>获取是否已连接</summary>
    public bool IsConnected => Volatile.Read(ref _started) != 0 && Volatile.Read(ref _disposed) == 0;
    /// <summary>获取是否已释放</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>初始化 Mock 平台适配器</summary>
    /// <param name="platformName">平台名称</param>
    public MockPlatformBotAdapter(string platformName = "mock") {
        PlatformName = platformName;
    }

    /// <summary>启动适配器</summary>
    /// <param name="ct">取消令牌</param>
    public ValueTask StartAsync(CancellationToken ct = default) {
        Interlocked.Exchange(ref _started, 1);
        return ValueTask.CompletedTask;
    }

    /// <summary>发送消息到指定目标</summary>
    /// <param name="targetId">目标 ID</param>
    /// <param name="text">消息文本</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>消息 ID</returns>
    public ValueTask<string?> SendAsync(string targetId, string text, CancellationToken ct = default) {
        SentMessages.Add((targetId, text));
        return ValueTask.FromResult<string?>($"mock-msg-{SentMessages.Count}");
    }

    /// <summary>接收消息流</summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>平台消息异步枚举</returns>
    public IAsyncEnumerable<PlatformMessage> ReceiveAsync(CancellationToken ct = default)
        => _receiveChannel.Reader.ReadAllAsync(ct);

    /// <summary>投递消息到接收通道</summary>
    /// <param name="message">平台消息</param>
    public void Deliver(PlatformMessage message) => _receiveChannel.Writer.TryWrite(message);

    /// <summary>释放资源</summary>
    public ValueTask DisposeAsync() {
        Interlocked.Exchange(ref _disposed, 1);
        _receiveChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}