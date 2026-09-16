namespace Core.Utils;

/// <summary>
/// NetworkMailbox 单元测试 — 用 MockPlatformBotAdapter 验证邮箱与平台适配器的交互。
/// </summary>
public class NetworkMailboxTest
{
    [Fact]
    public async Task StartAsync_应启动适配器并开始接收循环()
    {
        var adapter = new MockPlatformBotAdapter("test-platform");
        await using var mailbox = new NetworkMailbox(adapter);

        await mailbox.StartAsync();

        adapter.IsConnected.Should().BeTrue("适配器应已启动");
        mailbox.IsConnected.Should().BeTrue("邮箱应已连接");
        mailbox.PlatformName.Should().Be("test-platform");
    }

    [Fact]
    public async Task TellAsync_应本地投递且调用适配器发送()
    {
        var adapter = new MockPlatformBotAdapter("test");
        await using var mailbox = new NetworkMailbox(adapter);
        await mailbox.StartAsync();

        await mailbox.RegisterAgentAsync("agent-1", "session-1");
        var msg = new CoordinatorMessage { FromAgentId = "sender", ToAgentId = "agent-1", MessageType = "text", Content = "hello" };

        await mailbox.TellAsync("agent-1", msg);
        await Task.Delay(100);

        adapter.SentMessages.Should().ContainSingle(m => m.targetId == "agent-1" && m.text == "hello");
    }

    [Fact]
    public async Task TellBroadcastAsync_应对所有Agent发送()
    {
        var adapter = new MockPlatformBotAdapter("test");
        await using var mailbox = new NetworkMailbox(adapter);
        await mailbox.StartAsync();

        await mailbox.RegisterAgentAsync("agent-1", "s1");
        await mailbox.RegisterAgentAsync("agent-2", "s1");
        var msg = new CoordinatorMessage { FromAgentId = "sender", ToAgentId = "all", MessageType = "text", Content = "broadcast" };

        await mailbox.TellBroadcastAsync(msg, excludeAgentId: null);
        await Task.Delay(100);

        adapter.SentMessages.Should().HaveCount(2, "应向两个Agent各发送一次");
    }

    [Fact]
    public async Task TellBroadcastAsync_应排除指定Agent()
    {
        var adapter = new MockPlatformBotAdapter("test");
        await using var mailbox = new NetworkMailbox(adapter);
        await mailbox.StartAsync();

        await mailbox.RegisterAgentAsync("agent-1", "s1");
        await mailbox.RegisterAgentAsync("agent-2", "s1");
        var msg = new CoordinatorMessage { FromAgentId = "sender", ToAgentId = "all", MessageType = "text", Content = "broadcast" };

        await mailbox.TellBroadcastAsync(msg, excludeAgentId: "agent-1");
        await Task.Delay(100);

        adapter.SentMessages.Should().ContainSingle(m => m.targetId == "agent-2");
    }

    [Fact]
    public async Task 适配器收到消息_应投递到对应Agent()
    {
        var adapter = new MockPlatformBotAdapter("test");
        await using var mailbox = new NetworkMailbox(adapter);
        await mailbox.StartAsync();

        await mailbox.RegisterAgentAsync("agent-1", "s1");

        adapter.Deliver(new PlatformMessage("remote-user", "agent-1", "from-remote", DateTimeOffset.UtcNow));

        var received = await mailbox.ReceiveAsync("agent-1").FirstOrDefaultAsync();
        received.Should().NotBeNull();
        received!.Content.Should().Be("from-remote");
        received.FromAgentId.Should().Be("remote-user");
    }

    [Fact]
    public async Task DisposeAsync_应释放适配器()
    {
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
internal sealed class MockPlatformBotAdapter : IPlatformBotAdapter
{
    private readonly Channel<PlatformMessage> _receiveChannel = Channel.CreateUnbounded<PlatformMessage>();
    private int _started;
    private int _disposed;

    public string PlatformName { get; }
    public List<(string targetId, string text)> SentMessages { get; } = new();
    public bool IsConnected => Volatile.Read(ref _started) != 0 && Volatile.Read(ref _disposed) == 0;
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public MockPlatformBotAdapter(string platformName = "mock")
    {
        PlatformName = platformName;
    }

    public ValueTask StartAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _started, 1);
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> SendAsync(string targetId, string text, CancellationToken ct = default)
    {
        SentMessages.Add((targetId, text));
        return ValueTask.FromResult<string?>($"mock-msg-{SentMessages.Count}");
    }

    public IAsyncEnumerable<PlatformMessage> ReceiveAsync(CancellationToken ct = default)
        => _receiveChannel.Reader.ReadAllAsync(ct);

    public void Deliver(PlatformMessage message) => _receiveChannel.Writer.TryWrite(message);

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _receiveChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
