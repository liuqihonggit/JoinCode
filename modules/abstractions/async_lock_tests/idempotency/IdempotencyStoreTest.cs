namespace Core.Utils;

/// <summary>
/// IdempotencyStore 单元测试 — 验证无锁键控去重 + TTL 过期 + 并发安全。
/// </summary>
public class IdempotencyStoreTest {
    /// <summary>
    /// 首次注册返回 true，重复注册返回 false 且不覆盖。
    /// </summary>
    [Fact]
    public void TryRegister_FirstReturnsTrue_DuplicateReturnsFalse() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");

        store.TryRegister(key, "first").Should().BeTrue();
        store.TryRegister(key, "second").Should().BeFalse();

        store.TryGetResult<string>(key, out var result).Should().BeTrue();
        result.Should().Be("first");
    }

    /// <summary>
    /// 注册后能取回结果，未注册返回 false。
    /// </summary>
    [Fact]
    public void TryGetResult_RegisteredReturnsValue_UnregisteredReturnsFalse() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");

        store.TryGetResult<string>(key, out _).Should().BeFalse();

        store.TryRegister(key, 42);
        store.TryGetResult<int>(key, out var result).Should().BeTrue();
        result.Should().Be(42);
    }

    /// <summary>
    /// 类型不匹配时 TryGetResult 返回 false。
    /// </summary>
    [Fact]
    public void TryGetResult_TypeMismatch_ReturnsFalse() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");

        store.TryRegister(key, "string-value");
        store.TryGetResult<int>(key, out _).Should().BeFalse();
    }

    /// <summary>
    /// null 结果能正确缓存和取回。
    /// </summary>
    [Fact]
    public void TryGetResult_NullResult_CachedAndRetrieved() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");

        store.TryRegister<string?>(key, null);
        store.TryGetResult<string?>(key, out var result).Should().BeTrue();
        result.Should().BeNull();
    }

    /// <summary>
    /// IsRegistered — 注册前 false，注册后 true，Evict 后 false。
    /// </summary>
    [Fact]
    public void IsRegistered_And_Evict() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-1", "op-1");

        store.IsRegistered(key).Should().BeFalse();
        store.TryRegister(key, "value");
        store.IsRegistered(key).Should().BeTrue();

        store.Evict(key);
        store.IsRegistered(key).Should().BeFalse();
    }

    /// <summary>
    /// EvictExpired — 过期条目被清除，未过期保留。
    /// </summary>
    [Fact]
    public async Task EvictExpired_RemovesExpired_KeepsFresh() {
        var store = new IdempotencyStore();
        var key1 = new IdempotencyKey("flow-1", "op-1");
        var key2 = new IdempotencyKey("flow-2", "op-2");

        store.TryRegister(key1, "old");
        await Task.Delay(100);
        store.TryRegister(key2, "new");

        var evicted = store.EvictExpired(TimeSpan.FromMilliseconds(80));
        evicted.Should().Be(1);
        store.IsRegistered(key1).Should().BeFalse();
        store.IsRegistered(key2).Should().BeTrue();
    }

    /// <summary>
    /// 并发安全 — 多线程并发 TryRegister 同一 key，只有一个成功。
    /// </summary>
    [Fact]
    public async Task ConcurrentTryRegister_OnlyOneSucceeds() {
        var store = new IdempotencyStore();
        var key = new IdempotencyKey("flow-concurrent", "op-1");
        var successCount = 0;

        var tasks = Enumerable.Range(0, 32)
            .Select(i => Task.Run(() => {
                if (store.TryRegister(key, i))
                    Interlocked.Increment(ref successCount);
            }));
        await Task.WhenAll(tasks);

        successCount.Should().Be(1);
        store.IsRegistered(key).Should().BeTrue();
    }

    /// <summary>
    /// 并发安全 — 多线程并发注册不同 key，全部成功。
    /// </summary>
    [Fact]
    public async Task ConcurrentTryRegister_DifferentKeys_AllSucceed() {
        var store = new IdempotencyStore();
        var successCount = 0;

        var tasks = Enumerable.Range(0, 64)
            .Select(i => Task.Run(() => {
                var key = new IdempotencyKey($"flow-{i}", "op-1");
                if (store.TryRegister(key, i))
                    Interlocked.Increment(ref successCount);
            }));
        await Task.WhenAll(tasks);

        successCount.Should().Be(64);
    }

    /// <summary>
    /// IdempotencyKey.None 是空键，IsEmpty 为 true。
    /// </summary>
    [Fact]
    public void IdempotencyKey_None_IsEmpty() {
        IdempotencyKey.None.IsEmpty.Should().BeTrue();
        new IdempotencyKey("flow", "op").IsEmpty.Should().BeFalse();
    }

    /// <summary>
    /// IdempotencyKey 相等性 — 相同字段值相等。
    /// </summary>
    [Fact]
    public void IdempotencyKey_Equality() {
        var k1 = new IdempotencyKey("flow-1", "op-1");
        var k2 = new IdempotencyKey("flow-1", "op-1");
        var k3 = new IdempotencyKey("flow-1", "op-2");

        k1.Should().Be(k2);
        k1.Should().NotBe(k3);
        k1.GetHashCode().Should().Be(k2.GetHashCode());
    }
}
