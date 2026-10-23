namespace Infra.IO.Tests.Persistence;

/// <summary>
/// 短租约 KV 存储适配器测试 — 验证每次操作 Acquire→执行→Dispose(ADR 0139 短租约协议)。
/// <para>背景: gh 缓存(GitHubToolHandlers/GhCacheService)消费 IKvStore,若直接持有进程级 lease,
/// 长驻进程会饿死所有 CLI(锁被占→降级内存→缓存永远 miss)。适配器每次操作独立短租约解决此问题。</para>
/// <para>核心场景: 模拟两个 CLI 进程(两个 adapter 实例+独立 factory)先后读写同一目录 — 第二个必须命中。</para>
/// </summary>
public sealed class LeasedKvStoreAdapterTests {
    private static byte[] K(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public async Task Put_ThenNewAdapter_Get_HitsPersistedData() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_adapter_{Guid.NewGuid():N}");
        try {
            await using (var first = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir))) {
                await first.PutAsync(K("gh:log:1"), K("log-content"));
            }

            await using var second = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir));
            (await second.GetAsync(K("gh:log:1"))).Should().Equal(K("log-content"));
        } finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task Delete_NewAdapter_Get_ReturnsNull() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_adapter_{Guid.NewGuid():N}");
        try {
            await using (var first = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir))) {
                await first.PutAsync(K("k1"), K("v1"));
                await first.DeleteAsync(K("k1"));
            }

            await using var second = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir));
            (await second.GetAsync(K("k1"))).Should().BeNull();
        } finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task ScanAsync_NewAdapter_YieldsPersistedEntries() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_adapter_{Guid.NewGuid():N}");
        try {
            await using (var first = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir))) {
                await first.PutAsync(K("b"), K("2"));
                await first.PutAsync(K("a"), K("1"));
            }

            await using var second = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir));
            var pairs = new List<(byte[] Key, byte[] Value)>();
            await foreach (var kv in second.ScanAsync(K("a"), K("b"))) {
                pairs.Add(kv);
            }
            pairs.Count.Should().Be(2);
            S(pairs[0].Key).Should().Be("a");
            S(pairs[1].Key).Should().Be("b");
        } finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task TtlExtensions_ThroughAdapter_RoundTrip() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_adapter_{Guid.NewGuid():N}");
        try {
            await using (var first = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir))) {
                await first.PutWithTtlAsync(K("gh:api:repos"), K("body"), TimeSpan.FromMinutes(5));
            }

            await using var second = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir));
            (await second.GetWithTtlAndRenewAsync(K("gh:api:repos"), TimeSpan.FromMinutes(5))).Should().Equal(K("body"));
        } finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task ConcurrentPuts_AllPersist_NoInMemoryDegradation() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_adapter_{Guid.NewGuid():N}");
        try {
            await using var adapter = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir));
            var keys = Enumerable.Range(0, 8).Select(i => K($"k{i}")).ToArray();
            await Task.WhenAll(keys.Select((k, i) => adapter.PutAsync(k, K($"v{i}")).AsTask()));

            await using var verify = new LeasedKvStoreAdapter(new PithosKvStoreLeaseFactory(dir));
            for (var i = 0; i < keys.Length; i++) {
                (await verify.GetAsync(keys[i])).Should().Equal(K($"v{i}"), "并发写入不应因进程内防重入锁降级内存而丢失");
            }
        } finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task Operations_ReleaseLockBetweenCalls() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_adapter_{Guid.NewGuid():N}");
        try {
            var factory = new PithosKvStoreLeaseFactory(dir);
            await using var adapter = new LeasedKvStoreAdapter(factory);
            await adapter.PutAsync(K("k"), K("v"));

            using var lease = await factory.AcquireAsync();
            lease.IsInMemory.Should().BeFalse("适配器操作完成后必须释放锁(短租约),外部应能拿到磁盘租约");
        } finally {
            TryCleanupDir(dir);
        }
    }

    private static void TryCleanupDir(string dir) {
        if (Directory.Exists(dir)) {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { Console.Error.WriteLine($"[Test] cleanup {dir} failed: {ex.Message}"); }
        }
    }
}
