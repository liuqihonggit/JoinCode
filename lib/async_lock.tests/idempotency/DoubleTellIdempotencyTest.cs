namespace Core.Utils;

/// <summary>
/// 双 Tell 幂等集成测试 — 验证 ActorBase ConsumeLoop 对 IRequestCommand 的缓存命中/未命中行为。
/// <para>双 Tell 协议:发送方 Tell 请求(命令携带 OnSuccess/OnFailure 回调) → Consumer 检查缓存 → 命中:TryRestoreFromCache 调 OnSuccess / 未命中:HandleAsync 执行+缓存+调 OnSuccess</para>
/// </summary>
public class DoubleTellIdempotencyTest {
    /// <summary>首次发送命令 → 执行 HandleAsync → 缓存结果 → 调用 OnSuccess 回调</summary>
    [Fact]
    public async Task FirstSend_ExecutesHandleAsync_CachesResult_InvokesOnSuccess() {
        await using var actor = new IdempotentTestActor();
        var key = new IdempotencyKey("flow-1", "op-1");
        var (cmd, replyTask) = TestRequestCommand.Create(key, "payload-A");

        await actor.SendAsync(cmd);
        var reply = await replyTask;

        reply.Should().Be("result-payload-A");
        actor.HandleInvocationCount.Should().Be(1);
        actor.ExposedStore!.IsRegistered(key).Should().BeTrue();
    }

    /// <summary>重复发送相同幂等键 → 命中缓存 → 跳过 HandleAsync → 调用 OnSuccess 恢复回执</summary>
    [Fact]
    public async Task DuplicateSend_HitsCache_SkipsHandleAsync_RestoresReply() {
        await using var actor = new IdempotentTestActor();
        var key = new IdempotencyKey("flow-2", "op-2");
        var (cmd1, reply1Task) = TestRequestCommand.Create(key, "payload-B");
        var (cmd2, reply2Task) = TestRequestCommand.Create(key, "payload-B-dup");

        await actor.SendAsync(cmd1);
        var firstReply = await reply1Task;

        await actor.SendAsync(cmd2);
        var secondReply = await reply2Task;

        firstReply.Should().Be("result-payload-B");
        secondReply.Should().Be("result-payload-B");
        actor.HandleInvocationCount.Should().Be(1);
    }

    /// <summary>不同幂等键的命令 → 各自执行 HandleAsync,互不干扰</summary>
    [Fact]
    public async Task DifferentKeys_EachExecutesHandleAsync_NoCrossDedup() {
        await using var actor = new IdempotentTestActor();
        var (cmd1, reply1Task) = TestRequestCommand.Create(new IdempotencyKey("flow-3", "op-3"), "X");
        var (cmd2, reply2Task) = TestRequestCommand.Create(new IdempotencyKey("flow-4", "op-4"), "Y");

        await actor.SendAsync(cmd1);
        await actor.SendAsync(cmd2);

        var replies = await Task.WhenAll(reply1Task, reply2Task);

        replies[0].Should().Be("result-X");
        replies[1].Should().Be("result-Y");
        actor.HandleInvocationCount.Should().Be(2);
    }

    /// <summary>未设置 IdempotencyStore 时 → IRequestCommand 走正常 HandleAsync 路径</summary>
    [Fact]
    public async Task NoStoreSet_IRequestCommand_GoesThroughHandleAsync() {
        await using var actor = new IdempotentTestActor(enableIdempotency: false);
        var key = new IdempotencyKey("flow-5", "op-5");
        var (cmd1, reply1Task) = TestRequestCommand.Create(key, "Z");
        var (cmd2, reply2Task) = TestRequestCommand.Create(key, "Z");

        await actor.SendAsync(cmd1);
        await actor.SendAsync(cmd2);

        var replies = await Task.WhenAll(reply1Task, reply2Task);

        replies[0].Should().Be("result-Z");
        replies[1].Should().Be("result-Z");
        actor.HandleInvocationCount.Should().Be(2);
    }

    /// <summary>非 IRequestCommand 的普通命令 → 不受幂等守卫影响,正常执行</summary>
    [Fact]
    public async Task PlainCommand_NotAffectedByIdempotencyGuard() {
        await using var actor = new IdempotentTestActor();
        await actor.SendAsync(new PlainCommand("plain-1"));
        await actor.SendAsync(new PlainCommand("plain-2"));

        var replies = new List<string>();
        await foreach (var reply in actor.OutputAsync()) {
            replies.Add(reply);
            if (replies.Count >= 2) break;
        }

        replies.Should().Contain("plain-result-plain-1");
        replies.Should().Contain("plain-result-plain-2");
        actor.HandleInvocationCount.Should().Be(2);
    }

    /// <summary>并发发送相同幂等键 → HandleAsync 只执行一次(竞态下至多一次)</summary>
    [Fact]
    public async Task ConcurrentSameKey_HandleAsyncExecutedAtMostOnce() {
        await using var actor = new IdempotentTestActor();
        var key = new IdempotencyKey("flow-concurrent", "op-concurrent");
        const int count = 50;

        var cmds = new (TestRequestCommand Cmd, Task<string> ReplyTask)[count];
        for (var i = 0; i < count; i++)
            cmds[i] = TestRequestCommand.Create(key, "concurrent");

        var sends = cmds.Select(c => actor.SendAsync(c.Cmd).AsTask()).ToArray();
        await Task.WhenAll(sends);

        var replies = await Task.WhenAll(cmds.Select(c => c.ReplyTask));

        replies.Should().HaveCount(count);
        replies.Should().AllBe("result-concurrent");
        actor.HandleInvocationCount.Should().Be(1);
    }
}

/// <summary>
/// 测试用请求命令 — 实现 IRequestCommand&lt;string&gt;,携带幂等键 + 负载 + OnSuccess/OnFailure 回调。
/// </summary>
internal sealed record TestRequestCommand(
    IdempotencyKey IdempotencyKey,
    string Payload,
    Action<string> OnSuccess,
    Action<Exception> OnFailure,
    Action<BackpressureSignal> OnBackpressure
) : IRequestCommand<string> {
    /// <summary>创建命令 + 配套的 ReplyTask(通过 TCS 桥接回调,仅测试用)</summary>
    public static (TestRequestCommand Cmd, Task<string> ReplyTask) Create(IdempotencyKey key, string payload) {
        var tcs = new TaskCompletionSource<string>();
        var cmd = new TestRequestCommand(
            key, payload,
            tcs.SetResult,
            tcs.SetException,
            _ => { });
        return (cmd, tcs.Task);
    }

    /// <summary>从幂等缓存恢复结果 — 命中缓存时调用 OnSuccess 回调</summary>
    public bool TryRestoreFromCache(IIdempotencyStore store) {
        if (store.TryGetResult<string>(IdempotencyKey, out var cached) && cached is not null) {
            OnSuccess(cached);
            return true;
        }
        return false;
    }
}

/// <summary>
/// 测试用普通命令 — 不实现 IRequestCommand,不受幂等守卫影响。
/// </summary>
internal sealed record PlainCommand(string Payload);

/// <summary>
/// 幂等测试 Actor — 输入 TestRequestCommand | PlainCommand,输出 string 回执。
/// <para>设置 IdempotencyStore 后,对 IRequestCommand 做双 Tell 幂等去重。</para>
/// </summary>
internal sealed class IdempotentTestActor : ActorBase<object, string> {
    public int HandleInvocationCount { get; private set; }
    public IIdempotencyStore? ExposedStore => IdempotencyStore;

    public IdempotentTestActor(bool enableIdempotency = true) {
        if (enableIdempotency)
            IdempotencyStore = new IdempotencyStore();
    }

    protected override async ValueTask HandleAsync(object command, CancellationToken ct) {
        await Task.Yield();
        switch (command) {
            case TestRequestCommand req:
                HandleInvocationCount++;
                var result = $"result-{req.Payload}";
                IdempotencyStore?.TryRegister(req.IdempotencyKey, result);
                req.OnSuccess(result);
                break;
            case PlainCommand plain:
                HandleInvocationCount++;
                TryPublish($"plain-result-{plain.Payload}");
                break;
        }
    }
}
