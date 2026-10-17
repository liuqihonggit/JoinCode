namespace Infra.IO.Tests.Persistence;

/// <summary>
/// KV 存储租约工厂测试 — 验证 Acquire/Dispose/降级路径（ADR 0139）。
/// </summary>
public sealed class KvStoreLeaseFactoryTests {
    private static byte[] K(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public async Task Acquire_WhenMutexFree_ReturnsDiskLease() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_lease_{Guid.NewGuid():N}");
        try {
            var factory = new PithosKvStoreLeaseFactory(dir);
            using var lease = factory.Acquire();
            lease.IsInMemory.Should().BeFalse();
            await lease.PutAsync(K("k1"), K("v1"));
            (await lease.GetAsync(K("k1"))).Should().Equal(K("v1"));
        }
        finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task Acquire_WhenMutexHeld_FallsBackToInMemory() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_lease_{Guid.NewGuid():N}");
        var mutexName = PithosKvStoreLeaseFactory.GetMutexName(dir);
        var holder = new Mutex(false, mutexName, out _);
        using var release = new ManualResetEventSlim();
        using var acquired = new ManualResetEventSlim();
        var thread = new Thread(() => {
            holder.WaitOne();
            acquired.Set();
            release.Wait();
            holder.ReleaseMutex();
        }) { IsBackground = true };
        thread.Start();
        acquired.Wait();
        try {
            var factory = new PithosKvStoreLeaseFactory(dir);
            using var lease = factory.Acquire();
            lease.IsInMemory.Should().BeTrue();
            await lease.PutAsync(K("k"), K("v"));
            (await lease.GetAsync(K("k"))).Should().Equal(K("v"));
        }
        finally {
            release.Set();
            thread.Join();
            holder.Dispose();
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public async Task Dispose_ReleasesMutex_AllowingSubsequentDiskAcquire() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_lease_{Guid.NewGuid():N}");
        try {
            var factory = new PithosKvStoreLeaseFactory(dir);
            using (var lease1 = factory.Acquire()) {
                lease1.IsInMemory.Should().BeFalse();
                await lease1.PutAsync(K("persist"), K("val"));
            }

            using var lease2 = factory.Acquire();
            lease2.IsInMemory.Should().BeFalse();
            (await lease2.GetAsync(K("persist"))).Should().Equal(K("val"));
        }
        finally {
            TryCleanupDir(dir);
        }
    }

    [Fact]
    public void InMemoryFactory_Acquire_ReturnsInMemoryLease() {
        var factory = new InMemoryKvStoreLeaseFactory();
        using var lease = factory.Acquire();
        lease.IsInMemory.Should().BeTrue();
    }

    [Fact]
    public async Task Lease_Dispose_IsIdempotent() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_lease_{Guid.NewGuid():N}");
        try {
            var factory = new PithosKvStoreLeaseFactory(dir);
            var lease = factory.Acquire();
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
