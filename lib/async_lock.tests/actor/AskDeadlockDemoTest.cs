namespace Core.Utils;

/// <summary>
/// Demo: 对比 TCS vs 全双工输出管道+消息ID 在 Consumer 阻塞时的死锁行为。
/// <para>核心论点: 两种方案死锁根因相同 — Consumer 不运行就没有结果,无论结果通过 TCS 还是 Channel 传递。</para>
/// <para> - TCS 方案:    Consumer 不运行 → 不调 SetResult   → await tcs.Task           永等</para>
/// <para> - Channel 方案: Consumer 不运行 → 不调 TryPublish → await reader.ReadAsync  永等</para>
/// <para>结论: Channel+MsgId 不能解决死锁,因为结果传递方式不影响 Consumer 是否运行。</para>
/// <para>死锁根因是 Consumer(Task.Run)依赖线程池调度,线程池饥饿或 Consumer 阻塞时,命令不会被处理。</para>
/// </summary>
public class AskDeadlockDemoTest {
    /// <summary>
    /// 方案A(TCS): 正常情况 — Consumer 处理命令并 SetResult,AskAwait 成功返回。
    /// </summary>
    [Fact]
    public async Task TCS_NormalCase_ReturnsResult() {
        await using var actor = new DemoActor();
        var result = await actor.AskViaTcsAsync("hello");
        result.Should().Be("echo:hello");
    }

    /// <summary>
    /// 方案B(Channel+MsgId): 正常情况 — Consumer 处理命令并 TryPublish,reader 循环按 MsgId 分发结果。
    /// </summary>
    [Fact]
    public async Task ChannelMsgId_NormalCase_ReturnsResult() {
        await using var actor = new DemoActor();
        var result = await actor.AskViaChannelAsync("hello");
        result.Should().Be("echo:hello");
    }

    /// <summary>
    /// 方案A(TCS): Consumer 阻塞 — AskViaTcs 超时,因为 Consumer 不运行就不会 SetResult。
    /// </summary>
    [Fact]
    public async Task TCS_ConsumerBlocked_ThrowsDeadlockTimeout() {
        await using var actor = new DemoActor();
        actor.Gate = new TaskCompletionSource();

        var act = async () => await actor.AskViaTcsAsync("hello", timeoutMs: 500);
        await act.Should().ThrowAsync<ActorAskDeadlockException>();
    }

    /// <summary>
    /// 方案B(Channel+MsgId): Consumer 阻塞 — AskViaChannel 超时,因为 Consumer 不运行就不会 TryPublish。
    /// <para>⚠️ 这证明 Channel+MsgId 方案不能解决死锁 — 根因与 TCS 完全相同。</para>
    /// </summary>
    [Fact]
    public async Task ChannelMsgId_ConsumerBlocked_ThrowsTimeout() {
        await using var actor = new DemoActor();
        actor.Gate = new TaskCompletionSource();

        var act = async () => await actor.AskViaChannelAsync("hello", timeoutMs: 500);
        await act.Should().ThrowAsync<ActorAskDeadlockException>();
    }

    /// <summary>
    /// 并发对比: 正常情况下两种方案都能并发处理多个请求。
    /// </summary>
    [Fact]
    public async Task BothModes_ConcurrentRequests_AllSucceed() {
        await using var actor = new DemoActor();

        var tcsTasks = Enumerable.Range(0, 20)
            .Select(i => actor.AskViaTcsAsync($"tcs-{i}"))
            .ToArray();
        var channelTasks = Enumerable.Range(0, 20)
            .Select(i => actor.AskViaChannelAsync($"ch-{i}"))
            .ToArray();

        var tcsResults = await Task.WhenAll(tcsTasks);
        var channelResults = await Task.WhenAll(channelTasks);

        tcsResults.Should().HaveCount(20).And.OnlyContain(r => r.StartsWith("echo:tcs-"));
        channelResults.Should().HaveCount(20).And.OnlyContain(r => r.StartsWith("echo:ch-"));
    }
}

/// <summary>
/// Demo Actor — 支持两种 Ask 模式:
/// <para>1. TCS:       命令带 TCS,Consumer 处理后直接 SetResult</para>
/// <para>2. Channel:   命令带 MsgId,Consumer 处理后 TryPublish,后台 reader 按 MsgId 分发到 TCS</para>
/// </summary>
internal sealed class DemoActor : ActorBase<DemoCmd, DemoOutput> {
    public TaskCompletionSource? Gate;

    private readonly ConcurrentDictionary<long, TaskCompletionSource<string>> _tcsPending = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<string>> _channelPending = new();
    private long _nextMsgId;
    private int _readerStarted;

    /// <summary>
    /// TCS 方案 Ask — 发命令(带 TCS),Consumer 处理后 SetResult,await tcs.Task 等结果。
    /// </summary>
    public async Task<string> AskViaTcsAsync(string input, int timeoutMs = 30_000) {
        var msgId = Interlocked.Increment(ref _nextMsgId);
        var tcs = new TaskCompletionSource<string>();
        _tcsPending[msgId] = tcs;
        await SendAsync(new DemoCmd(input, msgId, AskMode.Tcs));
        return await AskAwait(tcs, timeoutMs: timeoutMs);
    }

    /// <summary>
    /// Channel+MsgId 方案 Ask — 发命令(带 MsgId),Consumer 处理后 TryPublish 到输出管道,
    /// 后台 reader 循环从输出管道读取,按 MsgId 找到对应 TCS 并 SetResult。
    /// </summary>
    public async Task<string> AskViaChannelAsync(string input, int timeoutMs = 30_000) {
        EnsureReaderStarted();
        var msgId = Interlocked.Increment(ref _nextMsgId);
        var tcs = new TaskCompletionSource<string>();
        _channelPending[msgId] = tcs;
        await SendAsync(new DemoCmd(input, msgId, AskMode.Channel));
        return await AskAwait(tcs, timeoutMs: timeoutMs);
    }

    private void EnsureReaderStarted() {
        if (Interlocked.CompareExchange(ref _readerStarted, 1, 0) != 0) return;
        _ = Task.Run(ReadOutputLoopAsync);
    }

    private async Task ReadOutputLoopAsync() {
        try {
            await foreach (var output in OutputAsync()) {
                if (_channelPending.TryRemove(output.MsgId, out var tcs)) {
                    tcs.TrySetResult(output.Result);
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"DemoActor reader loop exited: {ex.Message}");
        }
    }

    protected override async ValueTask HandleAsync(DemoCmd command, CancellationToken ct) {
        if (Gate is not null) await Gate.Task.WaitAsync(ct);

        var result = $"echo:{command.Input}";

        if (command.Mode == AskMode.Tcs) {
            if (_tcsPending.TryRemove(command.MsgId, out var tcs)) {
                tcs.TrySetResult(result);
            }
        } else {
            TryPublish(new DemoOutput(command.MsgId, result));
        }
    }
}

internal enum AskMode { Tcs, Channel }

internal sealed record DemoCmd(string Input, long MsgId, AskMode Mode);
internal sealed record DemoOutput(long MsgId, string Result);