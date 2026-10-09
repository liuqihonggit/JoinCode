// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Infra.IO.Tests.Persistence;

public sealed class PithosKvStoreTests {
    private static byte[] K(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public async Task PutGet_InMemory_ReturnsValue() {
        await using var store = PithosKvStore.OpenInMemory();
        await store.PutAsync(K("hello"), K("world"));
        var got = await store.GetAsync(K("hello"));
        got.Should().NotBeNull();
        S(got!).Should().Be("world");
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsNull() {
        await using var store = PithosKvStore.OpenInMemory();
        var got = await store.GetAsync(K("nonexistent"));
        got.Should().BeNull();
    }

    [Fact]
    public async Task Delete_ExistingKey_RemovesValue() {
        await using var store = PithosKvStore.OpenInMemory();
        await store.PutAsync(K("key1"), K("val1"));
        await store.DeleteAsync(K("key1"));
        var got = await store.GetAsync(K("key1"));
        got.Should().BeNull();
    }

    [Fact]
    public async Task Scan_All_ReturnsSortedEntries() {
        await using var store = PithosKvStore.OpenInMemory();
        for (var i = 0; i < 50; i++) {
            await store.PutAsync(K($"key:{i:D3}"), K($"val:{i}"));
        }
        var entries = new List<(byte[] Key, byte[] Value)>();
        await foreach (var entry in store.ScanAsync()) {
            entries.Add(entry);
        }
        entries.Should().HaveCount(50);
        S(entries[0].Key).Should().Be("key:000");
        S(entries[49].Key).Should().Be("key:049");
    }

    [Fact]
    public async Task Scan_Range_ReturnsOnlyMatchingEntries() {
        await using var store = PithosKvStore.OpenInMemory();
        for (var i = 0; i < 100; i++) {
            await store.PutAsync(K($"k{i:D3}"), K($"v{i}"));
        }
        var entries = new List<(byte[] Key, byte[] Value)>();
        await foreach (var entry in store.ScanAsync(from: K("k020"), to: K("k029"))) {
            entries.Add(entry);
        }
        entries.Should().HaveCount(10);
        S(entries[0].Key).Should().Be("k020");
        S(entries[9].Key).Should().Be("k029");
    }

    [Fact]
    public async Task Put_OverwriteExistingKey_UpdatesValue() {
        await using var store = PithosKvStore.OpenInMemory();
        await store.PutAsync(K("key"), K("old"));
        await store.PutAsync(K("key"), K("new"));
        var got = await store.GetAsync(K("key"));
        S(got!).Should().Be("new");
    }

    [Fact]
    public async Task Persistence_Disk_SurvivesReopen() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_test_{Guid.NewGuid():N}");
        try {
            await using (var store = new PithosKvStore(dir)) {
                await store.PutAsync(K("persist_key"), K("persist_val"));
                await store.PutAsync(K("persist_key2"), K("persist_val2"));
            }
            await using var store2 = new PithosKvStore(dir);
            var v1 = await store2.GetAsync(K("persist_key"));
            var v2 = await store2.GetAsync(K("persist_key2"));
            S(v1!).Should().Be("persist_val");
            S(v2!).Should().Be("persist_val2");
        }
        finally {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Persistence_Disk_DeleteSurvivesReopen() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_test_{Guid.NewGuid():N}");
        try {
            await using (var store = new PithosKvStore(dir)) {
                await store.PutAsync(K("del_key"), K("del_val"));
                await store.DeleteAsync(K("del_key"));
            }
            await using var store2 = new PithosKvStore(dir);
            var got = await store2.GetAsync(K("del_key"));
            got.Should().BeNull();
        }
        finally {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
