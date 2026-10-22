// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Abs.Tests;

/// <summary>
/// KvStoreTtlExtensions TTL 缓存扩展测试 — 验证 Put/Get/IsExpired/Unwrap + 续期功能。
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class KvStoreTtlExtensionsTests {

    [Fact]
    public async Task PutWithTtlAsync_Then_GetWithTtlAsync_ReturnsValue() {
        await using var store = new InMemoryKvStore();
        var key = Encoding.UTF8.GetBytes("test:key");
        var value = Encoding.UTF8.GetBytes("hello world");

        await store.PutWithTtlAsync(key, value, TimeSpan.FromMinutes(5), CancellationToken.None);
        var result = await store.GetWithTtlAsync(key, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().Equal(value);
    }

    [Fact]
    public async Task GetWithTtlAsync_Expired_ReturnsNull() {
        await using var store = new InMemoryKvStore();
        var key = Encoding.UTF8.GetBytes("test:expired");
        var value = Encoding.UTF8.GetBytes("data");

        await store.PutWithTtlAsync(key, value, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        await Task.Delay(50, CancellationToken.None);

        var result = await store.GetWithTtlAsync(key, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWithTtlAsync_NonExistent_ReturnsNull() {
        await using var store = new InMemoryKvStore();
        var key = Encoding.UTF8.GetBytes("test:missing");

        var result = await store.GetWithTtlAsync(key, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task PutWithTtlAsync_NullTtl_StoresRawValue() {
        await using var store = new InMemoryKvStore();
        var key = Encoding.UTF8.GetBytes("test:nottl");
        var value = Encoding.UTF8.GetBytes("raw data");

        await store.PutWithTtlAsync(key, value, null, CancellationToken.None);
        var raw = await store.GetAsync(key, CancellationToken.None);

        raw.Should().Equal(value);
    }

    [Fact]
    public async Task IsExpired_NotExpired_ReturnsFalse() {
        var expiry = DateTime.UtcNow.AddMinutes(5).Ticks;
        var wrapped = new byte[8 + 3];
        BitConverter.GetBytes(expiry).CopyTo(wrapped, 0);
        Encoding.UTF8.GetBytes("abc").CopyTo(wrapped, 8);

        KvStoreTtlExtensions.IsExpired(wrapped).Should().BeFalse();
    }

    [Fact]
    public async Task IsExpired_Expired_ReturnsTrue() {
        var expiry = DateTime.UtcNow.AddMilliseconds(-1).Ticks;
        var wrapped = new byte[8 + 3];
        BitConverter.GetBytes(expiry).CopyTo(wrapped, 0);
        Encoding.UTF8.GetBytes("abc").CopyTo(wrapped, 8);

        KvStoreTtlExtensions.IsExpired(wrapped).Should().BeTrue();
    }

    [Fact]
    public async Task IsExpired_InvalidTicks_ReturnsTrue() {
        var wrapped = new byte[8 + 3];
        BitConverter.GetBytes(long.MaxValue).CopyTo(wrapped, 0);
        Encoding.UTF8.GetBytes("abc").CopyTo(wrapped, 8);

        KvStoreTtlExtensions.IsExpired(wrapped).Should().BeTrue();
    }

    [Fact]
    public async Task Unwrap_StripsTimestampPrefix() {
        var wrapped = new byte[8 + 5];
        BitConverter.GetBytes(123L).CopyTo(wrapped, 0);
        Encoding.UTF8.GetBytes("hello").CopyTo(wrapped, 8);

        var result = KvStoreTtlExtensions.Unwrap(wrapped);
        result.Should().Equal(Encoding.UTF8.GetBytes("hello"));
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task GetWithTtlAndRenewAsync_StaleEntry_RenewsTtl() {
        await using var store = new InMemoryKvStore();
        var key = Encoding.UTF8.GetBytes("test:renew");
        var value = Encoding.UTF8.GetBytes("renewable");

        // 剩余寿命落入抑制窗口内（进入最后 1 分钟）→ 续期拉满
        await store.PutWithTtlAsync(key, value, TimeSpan.FromSeconds(30), CancellationToken.None);

        var rawBefore = await store.GetAsync(key, CancellationToken.None);
        rawBefore.Should().NotBeNull();

        var result = await store.GetWithTtlAndRenewAsync(key, TimeSpan.FromMinutes(10), CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Equal(value);

        var rawAfter = await store.GetAsync(key, CancellationToken.None);
        rawAfter.Should().NotBeNull();
        var expiryBefore = BitConverter.ToInt64(rawBefore!, 0);
        var expiryAfter = BitConverter.ToInt64(rawAfter!, 0);
        expiryAfter.Should().BeGreaterThan(expiryBefore, "寿命进入抑制窗口的条目续期后过期时间应更晚");
    }

    /// <summary>续期抑制窗口 — 写入后 1 分钟内的重复查询零写回（新鲜条目无需续期，
    /// 抑制 gh CLI 高频轮询的 LSM 写放大；过期时间戳保持不变）。</summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: rawBefore/rawAfter 已 NotBeNull 断言后取时间戳")]
    [Fact]
    public async Task GetWithTtlAndRenewAsync_FreshEntry_SuppressesRenew() {
        await using var store = new InMemoryKvStore();
        var key = Encoding.UTF8.GetBytes("test:renew-suppress");
        var value = Encoding.UTF8.GetBytes("fresh");

        await store.PutWithTtlAsync(key, value, TimeSpan.FromMinutes(5), CancellationToken.None);
        var rawBefore = await store.GetAsync(key, CancellationToken.None);
        rawBefore.Should().NotBeNull();

        var result = await store.GetWithTtlAndRenewAsync(key, TimeSpan.FromMinutes(5), CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Equal(value);

        var rawAfter = await store.GetAsync(key, CancellationToken.None);
        rawAfter.Should().NotBeNull();
        var expiryBefore = BitConverter.ToInt64(rawBefore!, 0);
        var expiryAfter = BitConverter.ToInt64(rawAfter!, 0);
        expiryAfter.Should().Be(expiryBefore, "写入 1 分钟内的新鲜条目不续期（抑制窗口内零写回）");
    }

    [Fact]
    public async Task GetWithTtlAndRenewAsync_Expired_ReturnsNull_NoRenew() {
        await using var store = new InMemoryKvStore();
        var key = Encoding.UTF8.GetBytes("test:expired-renew");
        var value = Encoding.UTF8.GetBytes("data");

        await store.PutWithTtlAsync(key, value, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        await Task.Delay(50, CancellationToken.None);

        var result = await store.GetWithTtlAndRenewAsync(key, TimeSpan.FromMinutes(10), CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWithTtlAndRenewAsync_NonExistent_ReturnsNull() {
        await using var store = new InMemoryKvStore();
        var key = Encoding.UTF8.GetBytes("test:missing-renew");

        var result = await store.GetWithTtlAndRenewAsync(key, TimeSpan.FromMinutes(10), CancellationToken.None);
        result.Should().BeNull();
    }
}
