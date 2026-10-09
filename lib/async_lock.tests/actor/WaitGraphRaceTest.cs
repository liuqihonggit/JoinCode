// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Utils;

/// <summary>
/// P1-3 WaitGraphScope 竞态测试 — 验证用 ImmutableDag 替代 Dag 消除并发修改竞态。
/// <para>根因: Dag&lt;T&gt; 内部用 Dictionary（非线程安全），多异步流共享同一实例并发修改竞态。</para>
/// <para>修复: _askWaitGraph 改为 AsyncLocal&lt;ImmutableDag&lt;string&gt;?&gt;，每次 EnterWaitGraph 创建新副本。</para>
/// </summary>
[Trait("Category", "Flaky")]
[Trait("Category", "Timing")]
public class WaitGraphRaceTest {
    /// <summary>
    /// P1-3 修复验证: _askWaitGraph 类型从 AsyncLocal&lt;Dag&lt;string&gt;?&gt; 改为 AsyncLocal&lt;ImmutableDag&lt;string&gt;?&gt;。
    /// <para>ImmutableDag 使用 CAS 无锁，多异步流共享同一实例不会崩溃。</para>
    /// </summary>
    [Fact]
    public void BugP13_WaitGraph_UsesImmutableDag_NotDag() {
        var field = typeof(ActorBase<P13EchoCmd, Unit>)
            .GetField("_askWaitGraph", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        field.FieldType.Should().Be(typeof(AsyncLocal<ImmutableDag<string>?>),
            "等待图改为 ImmutableDag（CAS 无锁线程安全），不再是可变 Dag（Dictionary 非线程安全）(P1-3 修复)");
    }

    /// <summary>
    /// P1-3 修复验证: 并发 AskAwait 各流独立 ImmutableDag 副本，不共享同一实例竞态。
    /// <para>场景: Proxy Actor 的 Handle 中 Task.Run 调用 Echo Actor 的 AskAwait，</para>
    /// <para>多个 Task.Run 继承同一 AsyncLocal 上下文。修复后每次创建副本，各流独立。</para>
    /// </summary>
    [Fact]
    public async Task BugP13_ConcurrentAskWaitGraph_NoSharedDagRace() {
        await using var echo = new P13EchoActor();
        await using var proxy = new P13ProxyActor(echo);

        var tasks = Enumerable.Range(0, 30)
            .Select(i => proxy.AskEchoAsync($"msg-{i}", timeoutMs: 8000))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        results.Should().HaveCount(30).And.OnlyContain(r => r.StartsWith("echo:"));
    }

    /// <summary>
    /// P1-3 修复验证: 嵌套 AskAwait 等待图正确隔离 — 外层 A→B 边不影响内层 C→D 边。
    /// </summary>
    [Fact]
    public async Task BugP13_NestedAskWaitGraph_ScopesIsolated() {
        await using var echo = new P13EchoActor();
        await using var proxy = new P13ProxyActor(echo);

        var result = await proxy.AskEchoAsync("nested-test", timeoutMs: 8000);
        result.Should().Be("echo:nested-test");
    }
}

internal sealed class P13EchoActor : ActorBase<P13EchoCmd, Unit> {
    protected override void Handle(P13EchoCmd command, CancellationToken ct) {
        command.Tcs.TrySetResult($"echo:{command.Message}");
    }

    public Task<string> AskEchoDirectAsync(string message, int timeoutMs = 5000) {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Tell(new P13EchoCmd(message, tcs));
        return AskAwait(tcs, timeoutMs: timeoutMs);
    }
}

internal sealed record P13EchoCmd(string Message, TaskCompletionSource<string> Tcs);

internal sealed class P13ProxyActor : ActorBase<P13ProxyCmd, Unit> {
    private readonly P13EchoActor _target;

    public P13ProxyActor(P13EchoActor target) => _target = target;

    protected override void Handle(P13ProxyCmd command, CancellationToken ct) {
        RegisterInFlight(Task.Run(async () => {
            try {
                var result = await _target.AskEchoDirectAsync(command.Message);
                command.Tcs.TrySetResult(result);
            } catch (Exception ex) {
                command.Tcs.TrySetException(ex);
            }
        }));
    }

    public async Task<string> AskEchoAsync(string message, int timeoutMs = 5000) {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Tell(new P13ProxyCmd(message, tcs));
        return await AskAwait(tcs, timeoutMs: timeoutMs);
    }
}

internal sealed record P13ProxyCmd(string Message, TaskCompletionSource<string> Tcs);
