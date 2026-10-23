namespace Infra.IO.Tests.Persistence;

/// <summary>
/// KV 存储租约工厂测试 — 验证 AcquireAsync/Dispose/降级路径（ADR 0139）。
/// <para>两锁联合：SemaphoreSlim(1,1) 进程内防重入 + CrossProcessLock 跨进程。</para>
/// </summary>
public sealed class KvStoreLeaseFactoryTests {
    private static byte[] K(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public async Task Acquire_WhenLockFree_ReturnsDiskLease() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_lease_{Guid.NewGuid():N}");
        try {
            var factory = new PithosKvStoreLeaseFactory(dir);
            using var lease = await factory.AcquireAsync();
            lease.IsInMemory.Should().BeFalse();
            await lease.PutAsync(K("k1"), K("v1"));
            (await lease.GetAsync(K("k1"))).Should().Equal(K("v1"));
        }
        finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task Acquire_WhenLockHeld_FallsBackToInMemory() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_lease_{Guid.NewGuid():N}");
        try {
            var factory = new PithosKvStoreLeaseFactory(dir);
            using var lease1 = await factory.AcquireAsync();
            lease1.IsInMemory.Should().BeFalse();

            using var lease2 = await factory.AcquireAsync();
            lease2.IsInMemory.Should().BeTrue();
            await lease2.PutAsync(K("k"), K("v"));
            (await lease2.GetAsync(K("k"))).Should().Equal(K("v"));
        }
        finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task Dispose_ReleasesLock_AllowingSubsequentDiskAcquire() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_lease_{Guid.NewGuid():N}");
        try {
            var factory = new PithosKvStoreLeaseFactory(dir);
            using (var lease1 = await factory.AcquireAsync()) {
                lease1.IsInMemory.Should().BeFalse();
                await lease1.PutAsync(K("persist"), K("val"));
            }

            using var lease2 = await factory.AcquireAsync();
            lease2.IsInMemory.Should().BeFalse();
            (await lease2.GetAsync(K("persist"))).Should().Equal(K("val"));
        }
        finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task InMemoryFactory_Acquire_ReturnsInMemoryLease() {
        var factory = new InMemoryKvStoreLeaseFactory();
        using var lease = await factory.AcquireAsync();
        lease.IsInMemory.Should().BeTrue();
    }

    [Fact]
    public async Task Lease_Dispose_IsIdempotent() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_lease_{Guid.NewGuid():N}");
        try {
            var factory = new PithosKvStoreLeaseFactory(dir);
            var lease = await factory.AcquireAsync();
            lease.Dispose();
            lease.Dispose();
        }
        finally {
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
