// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Infra.IO.Tests.Persistence;

public sealed class InMemoryKvStoreTests {
    private static byte[] K(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public async Task PutGet_ReturnsValue() {
        await using var store = new InMemoryKvStore();
        await store.PutAsync(K("key"), K("val"));
        var got = await store.GetAsync(K("key"));
        S(got!).Should().Be("val");
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull() {
        await using var store = new InMemoryKvStore();
        var got = await store.GetAsync(K("nope"));
        got.Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesEntry() {
        await using var store = new InMemoryKvStore();
        await store.PutAsync(K("k"), K("v"));
        await store.DeleteAsync(K("k"));
        (await store.GetAsync(K("k"))).Should().BeNull();
    }

    [Fact]
    public async Task Scan_All_ReturnsSorted() {
        await using var store = new InMemoryKvStore();
        for (var i = 0; i < 20; i++) {
            await store.PutAsync(K($"k{i:D3}"), K($"v{i}"));
        }
        var entries = new List<(byte[] Key, byte[] Value)>();
        await foreach (var e in store.ScanAsync()) {
            entries.Add(e);
        }
        entries.Should().HaveCount(20);
        S(entries[0].Key).Should().Be("k000");
        S(entries[19].Key).Should().Be("k019");
    }

    [Fact]
    public async Task Scan_Range_ReturnsMatching() {
        await using var store = new InMemoryKvStore();
        for (var i = 0; i < 100; i++) {
            await store.PutAsync(K($"k{i:D3}"), K($"v{i}"));
        }
        var entries = new List<(byte[] Key, byte[] Value)>();
        await foreach (var e in store.ScanAsync(from: K("k020"), to: K("k029"))) {
            entries.Add(e);
        }
        entries.Should().HaveCount(10);
    }
}
