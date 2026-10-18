// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
[assembly: SuppressMessage("FileSystemAbstraction", "JCC9001", Scope = "type", Target = "JoinCode.CodeIndex.Tests.ContentHashTests", Justification = "mmap 哈希测试需直接访问文件系统验证真实文件的 BOM 跳过行为")]
namespace JoinCode.CodeIndex.Tests;

public sealed class ContentHashTests {
    [Fact]
    public void ComputeContentHash_String_Empty_ReturnsKnownSha256() {
        var hash = HashUtility.ComputeContentHash(string.Empty);

        Assert.Equal(64, hash.Length);
        Assert.Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", hash);
    }

    [Fact]
    public void ComputeContentHash_String_KnownContent_IsDeterministic() {
        var hash1 = HashUtility.ComputeContentHash("hello world");
        var hash2 = HashUtility.ComputeContentHash("hello world");

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public void ComputeContentHash_String_DifferentContent_DifferentHash() {
        var hash1 = HashUtility.ComputeContentHash("a");
        var hash2 = HashUtility.ComputeContentHash("b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeContentHash_Utf8Span_MatchesStringHash() {
        var content = "hello world";
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        var fromString = HashUtility.ComputeContentHash(content);
        var fromSpan = HashUtility.ComputeContentHash(bytes);

        Assert.Equal(fromString, fromSpan);
    }

    [Fact]
    public async Task ReadFileAndComputeHashAsync_ExistingFile_ReturnsContentAndHash() {
        await using var fs = new InMemoryFileSystem();
        await fs.WriteAllText("/tmp/sample.cs", "public class Foo { }");

        var (content, hash) = await HashUtility.ReadFileAndComputeHashAsync("/tmp/sample.cs", fs, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("public class Foo { }", content);
        Assert.Equal(64, hash.Length);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void ComputeContentHash_NullString_Throws() {
        Assert.Throws<ArgumentNullException>(() => HashUtility.ComputeContentHash((string)null!));
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Trait("Category", "Deterministic")]
    [Fact]
    public async Task ReadFileAndComputeHashAsync_NullFilePath_ThrowsArgumentNullException() {
        await using var fs = new InMemoryFileSystem();
        await Assert.ThrowsAsync<ArgumentNullException>(() => HashUtility.ReadFileAndComputeHashAsync(null!, fs, CancellationToken.None)).ConfigureAwait(true);
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public async Task ReadFileAndComputeHashAsync_EmptyFilePath_ThrowsArgumentException() {
        await using var fs = new InMemoryFileSystem();
        await Assert.ThrowsAsync<ArgumentException>(() => HashUtility.ReadFileAndComputeHashAsync("", fs, CancellationToken.None)).ConfigureAwait(true);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Trait("Category", "Deterministic")]
    [Fact]
    public async Task ReadFileAndComputeHashAsync_NullFileSystem_ThrowsArgumentNullException() {
        await Assert.ThrowsAsync<ArgumentNullException>(() => HashUtility.ReadFileAndComputeHashAsync("/tmp/x.cs", null!, CancellationToken.None)).ConfigureAwait(true);
    }

    // ===== ReadFileAndComputeHashMappedAsync BOM 跳过（改动 P5）=====

    /// <summary>
    /// 带 UTF-8 BOM 的文件经 mmap 读取后，content 不应以 BOM 前缀（U+FEFF）开头，且包含预期文本。
    /// </summary>
    [Trait("Category", "Deterministic")]
    [Fact]
    public async Task 带Bom的文件_Mapped读取后_Content不含Bom前缀() {
        var tempFile = Path.Combine(Path.GetTempPath(), $"bom_test_{Guid.NewGuid():N}.cs");
        try {
            // 写入 UTF-8 BOM + ASCII 内容
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var contentBytes = System.Text.Encoding.UTF8.GetBytes("public class Foo { }");
            await File.WriteAllBytesAsync(tempFile, [.. bom, .. contentBytes]).ConfigureAwait(true);

            var (content, hash) = await HashUtility.ReadFileAndComputeHashMappedAsync(tempFile, CancellationToken.None).ConfigureAwait(true);

            // content 不应以 BOM 字符（U+FEFF）开头 — 这是改动 P5 的核心断言
            content.Should().NotStartWith("\uFEFF");
            // content 应包含预期文本（mmap 视图可能页面大小对齐产生尾部填充，故用 StartWith）
            content.Should().StartWith("public class Foo { }");
            hash.Should().HaveLength(64);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 不带 BOM 的文件经 mmap 读取后，content 应以文件原始文本开头。
    /// </summary>
    [Trait("Category", "Deterministic")]
    [Fact]
    public async Task 不带Bom的文件_Mapped读取后_Content以原文开头() {
        var tempFile = Path.Combine(Path.GetTempPath(), $"nobom_test_{Guid.NewGuid():N}.cs");
        try {
            const string expected = "public class Bar { }";
            await File.WriteAllTextAsync(tempFile, expected, System.Text.Encoding.UTF8).ConfigureAwait(true);

            var (content, hash) = await HashUtility.ReadFileAndComputeHashMappedAsync(tempFile, CancellationToken.None).ConfigureAwait(true);

            content.Should().StartWith(expected);
            hash.Should().HaveLength(64);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 带 BOM 的中文内容文件经 mmap 读取后，content 应正确解码且不含 BOM 前缀。
    /// </summary>
    [Trait("Category", "Deterministic")]
    [Fact]
    public async Task 带Bom的中文文件_Mapped读取后_Content正确解码() {
        var tempFile = Path.Combine(Path.GetTempPath(), $"bom_cn_test_{Guid.NewGuid():N}.cs");
        try {
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var contentBytes = System.Text.Encoding.UTF8.GetBytes("// 中文注释 public class 中文类 { }");
            await File.WriteAllBytesAsync(tempFile, [.. bom, .. contentBytes]).ConfigureAwait(true);

            var (content, hash) = await HashUtility.ReadFileAndComputeHashMappedAsync(tempFile, CancellationToken.None).ConfigureAwait(true);

            content.Should().NotStartWith("\uFEFF");
            content.Should().StartWith("// 中文注释 public class 中文类 { }");
            hash.Should().HaveLength(64);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 带 BOM 与不带 BOM 的相同文本内容，BOM 被跳过后两者的 content 有效前缀一致。
    /// <para>注：mmap 视图按页面大小对齐，不同文件大小的尾部填充可能不同，故只比较有效文本前缀。</para>
    /// </summary>
    [Trait("Category", "Deterministic")]
    [Fact]
    public async Task 带Bom与不带Bom的相同内容_Mapped读取后_有效前缀一致() {
        var withBomFile = Path.Combine(Path.GetTempPath(), $"with_bom_{Guid.NewGuid():N}.cs");
        var withoutBomFile = Path.Combine(Path.GetTempPath(), $"without_bom_{Guid.NewGuid():N}.cs");
        try {
            const string text = "public class Consistent { }";
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var contentBytes = System.Text.Encoding.UTF8.GetBytes(text);
            await File.WriteAllBytesAsync(withBomFile, [.. bom, .. contentBytes]).ConfigureAwait(true);
            await File.WriteAllBytesAsync(withoutBomFile, contentBytes).ConfigureAwait(true);

            var (contentWithBom, hashWithBom) = await HashUtility.ReadFileAndComputeHashMappedAsync(withBomFile, CancellationToken.None).ConfigureAwait(true);
            var (contentNoBom, hashNoBom) = await HashUtility.ReadFileAndComputeHashMappedAsync(withoutBomFile, CancellationToken.None).ConfigureAwait(true);

            // 两者都不以 BOM 开头
            contentWithBom.Should().NotStartWith("\uFEFF");
            contentNoBom.Should().NotStartWith("\uFEFF");
            // 两者的有效文本前缀一致（BOM 被跳过）
            contentWithBom.Should().StartWith(text);
            contentNoBom.Should().StartWith(text);
            // hash 都是 64 位十六进制
            hashWithBom.Should().HaveLength(64);
            hashNoBom.Should().HaveLength(64);
        } finally {
            if (File.Exists(withBomFile)) File.Delete(withBomFile);
            if (File.Exists(withoutBomFile)) File.Delete(withoutBomFile);
        }
    }
}