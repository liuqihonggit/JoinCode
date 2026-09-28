
namespace Core.Tests.Memdir;

/// <summary>
/// SyncFileHash 哈希纯计算确定性测试
/// 算法: Java-style hash = ((hash << 5) - hash) + c, hash &amp;= 0x7FFFFFFF, 返回 hash.ToString("x8")
/// </summary>
public sealed class SyncFileHashTests {
    private readonly IO.FileSystem.InMemoryFileSystem _fs = new();

    private static string ExpectedHash(string content) {
        var hash = 0;
        foreach (var c in content) {
            hash = ((hash << 5) - hash) + c;
            hash &= 0x7FFFFFFF;
        }
        return hash.ToString("x8");
    }

    [Fact]
    public async Task ComputeAsync_EmptyContent_ReturnsZeroHash() {
        var path = "/test/sync/empty.txt";
        await _fs.WriteAllTextAsync(path, string.Empty);
        var result = await SyncFileHash.ComputeAsync(_fs, path).ConfigureAwait(true);
        result.Should().Be("00000000");
    }

    [Fact]
    public async Task ComputeAsync_SingleChar_ReturnsCharHashCode() {
        var path = "/test/sync/a.txt";
        await _fs.WriteAllTextAsync(path, "a");
        var result = await SyncFileHash.ComputeAsync(_fs, path).ConfigureAwait(true);
        // 'a' = 97, 97.ToString("x8") = "00000061"
        result.Should().Be("00000061");
    }

    [Fact]
    public async Task ComputeAsync_AbcContent_ReturnsExpectedHash() {
        var path = "/test/sync/abc.txt";
        await _fs.WriteAllTextAsync(path, "abc");
        var result = await SyncFileHash.ComputeAsync(_fs, path).ConfigureAwait(true);
        // 手算: 96354 & 0x7FFFFFFF = 96354 = 0x17862 → "00017862"
        result.Should().Be("00017862");
        result.Should().Be(ExpectedHash("abc"));
    }

    [Fact]
    public async Task ComputeAsync_FileNotExists_ReturnsEmptyString() {
        var result = await SyncFileHash.ComputeAsync(_fs, "/nonexistent/file.txt").ConfigureAwait(true);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeAsync_Deterministic_SameContentProducesSameHash() {
        var p1 = "/test/sync/det1.txt";
        var p2 = "/test/sync/det2.txt";
        var content = "deterministic content for hash test";
        await _fs.WriteAllTextAsync(p1, content);
        await _fs.WriteAllTextAsync(p2, content);

        var h1 = await SyncFileHash.ComputeAsync(_fs, p1).ConfigureAwait(true);
        var h2 = await SyncFileHash.ComputeAsync(_fs, p2).ConfigureAwait(true);
        h1.Should().Be(h2);
    }

    [Fact]
    public async Task ComputeAsync_DifferentContent_ProducesDifferentHash() {
        var p1 = "/test/sync/diff1.txt";
        var p2 = "/test/sync/diff2.txt";
        await _fs.WriteAllTextAsync(p1, "content one");
        await _fs.WriteAllTextAsync(p2, "content two");

        var h1 = await SyncFileHash.ComputeAsync(_fs, p1).ConfigureAwait(true);
        var h2 = await SyncFileHash.ComputeAsync(_fs, p2).ConfigureAwait(true);
        h1.Should().NotBe(h2);
    }

    [Fact]
    public async Task ComputeAsync_UnicodeContent_ProducesValidHexHash() {
        var path = "/test/sync/unicode.txt";
        await _fs.WriteAllTextAsync(path, "你好世界");
        var result = await SyncFileHash.ComputeAsync(_fs, path).ConfigureAwait(true);
        // 应为 8 位十六进制
        result.Length.Should().Be(8);
        result.Should().MatchRegex("^[0-9a-f]{8}$");
        result.Should().Be(ExpectedHash("你好世界"));
    }

    [Fact]
    public async Task ComputeAsync_HashAlwaysFitsInInt31Range() {
        // 长内容确保 hash 溢出后被 mask 截断到 31 位
        var path = "/test/sync/long.txt";
        await _fs.WriteAllTextAsync(path, new string('x', 10000));
        var result = await SyncFileHash.ComputeAsync(_fs, path).ConfigureAwait(true);
        result.Length.Should().Be(8);
        // 解析为 int 应非负(0x7FFFFFFF 范围内)
        var value = Convert.ToInt32(result, 16);
        value.Should().BeGreaterThanOrEqualTo(0);
        value.Should().BeLessThanOrEqualTo(0x7FFFFFFF);
    }

    [Fact]
    public async Task ComputeAsync_OrderMatters_PermutedContentProducesDifferentHash() {
        var p1 = "/test/sync/order1.txt";
        var p2 = "/test/sync/order2.txt";
        await _fs.WriteAllTextAsync(p1, "abc");
        await _fs.WriteAllTextAsync(p2, "cba");

        var h1 = await SyncFileHash.ComputeAsync(_fs, p1).ConfigureAwait(true);
        var h2 = await SyncFileHash.ComputeAsync(_fs, p2).ConfigureAwait(true);
        h1.Should().NotBe(h2);
    }
}
