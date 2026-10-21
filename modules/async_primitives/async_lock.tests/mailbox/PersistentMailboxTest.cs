namespace Core.Utils;

/// <summary>
/// PersistentMailbox 单元测试 — 验证持久化发送、确认、崩溃恢复。
/// </summary>
public class PersistentMailboxTest {
    [Fact]
    public async Task PersistentSendAsync_PersistsAndEnqueues() {
        var store = new InMemoryPersistentStore<string>();
        await using var actor = new SimpleTestActor();
        await using var mailbox = new PersistentMailbox<string, Unit>(actor, store, "actor-1");

        var tcs = new TaskCompletionSource<string>();
        await mailbox.PersistentSendAsync("hello");

        mailbox.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task AckAsync_DecreasesPendingCount() {
        var store = new InMemoryPersistentStore<string>();
        await using var actor = new SimpleTestActor();
        await using var mailbox = new PersistentMailbox<string, Unit>(actor, store, "actor-1");

        await mailbox.PersistentSendAsync("msg-1");
        await mailbox.AckAsync("msg-1");

        mailbox.PendingCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Flaky")]
    public async Task RecoverAsync_ReplaysPendingMessages() {
        var store = new InMemoryPersistentStore<string>();
        await using var actor = new SimpleTestActor();
        await using var mailbox = new PersistentMailbox<string, Unit>(actor, store, "actor-1");

        await mailbox.PersistentSendAsync("msg-1");
        await mailbox.PersistentSendAsync("msg-2");
        await mailbox.PersistentSendAsync("msg-3");

        var processed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        actor.OnMessage = msg => processed.Enqueue(msg);

        await mailbox.RecoverAsync();
        // 状态收敛等待（禁睡规范）：回放经 Actor 邮箱异步处理，固定 Task.Delay(100) 在本机
        // 全量并行高压下不够 → 偶发丢断言（8 轮全量 1 红实证）；10s 窗口轮询到三条齐即返回
        var converged = System.Threading.SpinWait.SpinUntil(
            () => processed.Count >= 3,
            TimeSpan.FromSeconds(10));
        converged.Should().BeTrue("10s 窗口内应回放完成（本机并行高压兜底）");
        processed.Should().Contain(new[] { "msg-1", "msg-2", "msg-3" });
    }

    [Fact]
    public async Task DisposeAsync_DisposesUnderlyingActor() {
        var store = new InMemoryPersistentStore<string>();
        var actor = new SimpleTestActor();
        var mailbox = new PersistentMailbox<string, Unit>(actor, store, "actor-1");

        await mailbox.DisposeAsync();

        var act = async () => await mailbox.PersistentSendAsync("msg");
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task InMemoryStore_LoadPending_ReturnsAllPersisted() {
        var store = new InMemoryPersistentStore<string>();
        await store.PersistAsync("a", "msg-1", default);
        await store.PersistAsync("a", "msg-2", default);

        var results = new List<string>();
        await foreach (var cmd in store.LoadPendingAsync("a", default))
            results.Add(cmd);

        results.Should().HaveCount(2);
    }
}

/// <summary>简单测试 Actor — 接收 string 命令,可选回调</summary>
internal sealed class SimpleTestActor : ActorBase<string, Unit> {
    public Action<string>? OnMessage;

    protected override void Handle(string command, CancellationToken ct) {
        OnMessage?.Invoke(command);
    }
}