namespace Core.Utils;

/// <summary>
/// AskWithRetryAsync 幂等集成测试 — 验证键控去重与 Actor 重试的集成。
/// </summary>
public class IdempotencyAskIntegrationTest {
    /// <summary>
    /// 第一次调用执行命令并缓存结果,第二次相同幂等键直接返回缓存(不执行命令)。
    /// </summary>
    [Fact]
    public async Task SameKey_SecondCallReturnsCache_WithoutExecuting() {
        await using var actor = new IdempotentAskActor();
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");

        var result1 = await actor.AskWithIdempotencyAsync(store, key, singleTimeoutMs: 2000);
        result1.Should().Be("result-1");
        actor.CallCount.Should().Be(1);

        var result2 = await actor.AskWithIdempotencyAsync(store, key, singleTimeoutMs: 2000);
        result2.Should().Be("result-1");
        actor.CallCount.Should().Be(1, "第二次应从缓存返回,不执行命令");
    }

    /// <summary>
    /// 不同幂等键各自独立执行和缓存。
    /// </summary>
    [Fact]
    public async Task DifferentKeys_EachExecutesIndependently() {
        await using var actor = new IdempotentAskActor();
        var store = new IdempotencyStore();
        var key1 = new IdempotencyKey("flow-1", "op-1");
        var key2 = new IdempotencyKey("flow-2", "op-1");

        var result1 = await actor.AskWithIdempotencyAsync(store, key1, singleTimeoutMs: 2000);
        var result2 = await actor.AskWithIdempotencyAsync(store, key2, singleTimeoutMs: 2000);

        result1.Should().Be("result-1");
        result2.Should().Be("result-2");
        actor.CallCount.Should().Be(2);
    }

    /// <summary>
    /// 不提供幂等存储时,行为不变(每次都执行命令)。
    /// </summary>
    [Fact]
    public async Task NoStore_AlwaysExecutes() {
        await using var actor = new IdempotentAskActor();
        var key = new IdempotencyKey("flow-1", "op-1");

        var result1 = await actor.AskWithIdempotencyAsync(null, key, singleTimeoutMs: 2000);
        var result2 = await actor.AskWithIdempotencyAsync(null, key, singleTimeoutMs: 2000);

        result1.Should().Be("result-1");
        result2.Should().Be("result-2");
        actor.CallCount.Should().Be(2);
    }

    /// <summary>
    /// 空键时,即使提供了 store 也不启用去重(每次都执行)。
    /// </summary>
    [Fact]
    public async Task EmptyKey_NoDedup() {
        await using var actor = new IdempotentAskActor();
        var store = new IdempotencyStore();

        var result1 = await actor.AskWithIdempotencyAsync(store, IdempotencyKey.None, singleTimeoutMs: 2000);
        var result2 = await actor.AskWithIdempotencyAsync(store, IdempotencyKey.None, singleTimeoutMs: 2000);

        result1.Should().Be("result-1");
        result2.Should().Be("result-2");
        actor.CallCount.Should().Be(2);
    }
}

/// <summary>
/// 幂等 Ask 测试 Actor — 每次处理命令时递增 CallCount 并返回带计数的结果。
/// </summary>
internal sealed class IdempotentAskActor : ActorBase<IdempotentAskCmd, Unit> {
    private int _callCount;

    /// <summary>命令处理次数</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>带幂等去重的 Ask 调用</summary>
    public Task<string> AskWithIdempotencyAsync(IIdempotencyStore? store, IdempotencyKey key, int singleTimeoutMs = 2000, CancellationToken ct = default)
        => AskWithRetryAsync<string>(tcs => new IdempotentAskCmd(tcs), ct, singleTimeoutMs, 16, store, key);

    /// <summary>处理命令 — 递增计数并设置结果</summary>
    protected override ValueTask HandleAsync(IdempotentAskCmd command, CancellationToken ct) {
        var count = Interlocked.Increment(ref _callCount);
        command.Tcs.TrySetResult($"result-{count}");
        return ValueTask.CompletedTask;
    }
}

internal sealed record IdempotentAskCmd(TaskCompletionSource<string> Tcs);
