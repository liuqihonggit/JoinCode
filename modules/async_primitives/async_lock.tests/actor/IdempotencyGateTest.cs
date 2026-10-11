namespace Core.Utils;

/// <summary>
/// IdempotencyGate 确定性单元测试 — 验证幂等缓存命中判断,不依赖时序/不启动 Actor。
/// <para>覆盖:未启用、null 命令、非 IRequestCommand、未命中缓存、命中缓存、IsEnabled 属性。</para>
/// </summary>
public class IdempotencyGateTest {
    /// <summary>store=null → IsEnabled=false,TryRestore 恒返回 false</summary>
    [Fact]
    public void NullStore_IsDisabled_TryRestoreAlwaysFalse() {
        var gate = new IdempotencyGate(null);
        gate.IsEnabled.Should().BeFalse();

        var key = new IdempotencyKey("f", "o");
        var (cmd, _) = TestRequestCommand.Create(key, "payload");

        gate.TryRestore(cmd).Should().BeFalse("未启用幂等存储,任何命令都走 Handle");
    }

    /// <summary>store 非 null → IsEnabled=true</summary>
    [Fact]
    public void NonNullStore_IsEnabled() {
        var gate = new IdempotencyGate(new IdempotencyStore());
        gate.IsEnabled.Should().BeTrue();
    }

    /// <summary>cmd=null → 返回 false(不抛 NRE)</summary>
    [Fact]
    public void NullCommand_ReturnsFalse() {
        var gate = new IdempotencyGate(new IdempotencyStore());
        gate.TryRestore(null).Should().BeFalse("null 命令直接返回 false,不抛异常");
    }

    /// <summary>cmd 不是 IRequestCommand → 返回 false</summary>
    [Fact]
    public void NonRequestCommand_ReturnsFalse() {
        var gate = new IdempotencyGate(new IdempotencyStore());
        var plain = new PlainCommand("plain");

        gate.TryRestore(plain).Should().BeFalse("普通命令不实现 IRequestCommand,不走幂等路径");
    }

    /// <summary>cmd 是 IRequestCommand 但 store 无缓存 → TryRestoreFromCache 返回 false → TryRestore 返回 false</summary>
    [Fact]
    public void RequestCommand_NoCacheHit_ReturnsFalse() {
        var store = new IdempotencyStore();
        var gate = new IdempotencyGate(store);
        var key = new IdempotencyKey("flow", "op");
        var (cmd, _) = TestRequestCommand.Create(key, "payload");

        gate.TryRestore(cmd).Should().BeFalse("缓存未命中,需执行 Handle");
        store.IsRegistered(key).Should().BeFalse();
    }

    /// <summary>cmd 是 IRequestCommand 且 store 已缓存结果 → TryRestore 返回 true 并触发 OnSuccess</summary>
    [Fact]
    public async Task RequestCommand_CacheHit_ReturnsTrue_InvokesOnSuccess() {
        var store = new IdempotencyStore();
        var gate = new IdempotencyGate(store);
        var key = new IdempotencyKey("flow", "op");
        var (cmd, replyTask) = TestRequestCommand.Create(key, "payload");

        store.TryRegister(key, "cached-result");

        gate.TryRestore(cmd).Should().BeTrue("缓存命中,跳过 Handle");
        replyTask.IsCompleted.Should().BeTrue("TryRestoreFromCache 调用 OnSuccess 回调");
        (await replyTask).Should().Be("cached-result");
    }

    /// <summary>命中缓存后 store.IsRegistered 为 true</summary>
    [Fact]
    public void CacheHit_StoreRegistered() {
        var store = new IdempotencyStore();
        var gate = new IdempotencyGate(store);
        var key = new IdempotencyKey("f2", "o2");
        var (cmd, _) = TestRequestCommand.Create(key, "p");
        store.TryRegister(key, "r");

        gate.TryRestore(cmd);

        store.IsRegistered(key).Should().BeTrue();
    }

    /// <summary>不同幂等键不互相命中</summary>
    [Fact]
    public void DifferentKeys_NoCrossHit() {
        var store = new IdempotencyStore();
        var gate = new IdempotencyGate(store);
        var keyA = new IdempotencyKey("fa", "oa");
        var keyB = new IdempotencyKey("fb", "ob");
        store.TryRegister(keyA, "result-a");

        var (cmdB, _) = TestRequestCommand.Create(keyB, "payload-b");

        gate.TryRestore(cmdB).Should().BeFalse("不同幂等键不命中");
    }
}
