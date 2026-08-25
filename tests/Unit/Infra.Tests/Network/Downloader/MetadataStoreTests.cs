namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// MetadataStore 单元测试 — 验证元数据读写:保存/加载/删除/损坏JSON/URL/ETag/LastModified 校验
/// <para>用 InMemoryFileSystem 纯内存,零磁盘 IO</para>
/// </summary>
public sealed class MetadataStoreTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly MetadataStore _store;
    private const string FilePath = "/tmp/download.bin";

    public MetadataStoreTests()
    {
        _store = new MetadataStore(_fs);
    }

    // === 保存/加载 ===

    [Fact]
    public void Save_Load_RoundTrip_PreservesData()
    {
        var metadata = BuildMetadata(url: "https://example.com/file.zip", totalLength: 1024 * 1024, eTag: "\"abc123\"");
        metadata.Chunks =
        [
            new() { Index = 0, Start = 0, End = 511, Downloaded = 512, Completed = true },
            new() { Index = 1, Start = 512, End = 1023, Downloaded = 100, Completed = false }
        ];

        _store.Save(FilePath, metadata);
        var loaded = _store.TryLoad(FilePath);

        loaded.Should().NotBeNull();
        loaded!.Url.Should().Be("https://example.com/file.zip");
        loaded.TotalLength.Should().Be(1024 * 1024);
        loaded.ETag.Should().Be("\"abc123\"");
        loaded.Chunks.Should().HaveCount(2);
        loaded.Chunks[0].Completed.Should().BeTrue();
        loaded.Chunks[1].Downloaded.Should().Be(100);
    }

    // === 不存在 ===

    [Fact]
    public void TryLoad_NotExists_ReturnsNull()
    {
        var result = _store.TryLoad(FilePath);
        result.Should().BeNull();
    }

    // === 损坏 JSON ===

    [Fact]
    public void TryLoad_CorruptJson_ReturnsNull()
    {
        var metaPath = MetadataStore.GetMetadataPath(FilePath);
        _fs.WriteAllText(metaPath, "{ this is not valid json }}}");

        var result = _store.TryLoad(FilePath);
        result.Should().BeNull();
    }

    // === 删除 ===

    [Fact]
    public void Delete_ExistingFile_RemovesIt()
    {
        _store.Save(FilePath, BuildMetadata("https://example.com", 100));
        var metaPath = MetadataStore.GetMetadataPath(FilePath);
        _fs.FileExists(metaPath).Should().BeTrue();

        _store.Delete(FilePath);

        _fs.FileExists(metaPath).Should().BeFalse();
    }

    [Fact]
    public void Delete_NonExisting_DoesNotThrow()
    {
        var act = () => _store.Delete(FilePath);
        act.Should().NotThrow();
    }

    // === Matches:URL 校验 ===

    [Fact]
    public void Matches_UrlMismatch_ReturnsFalse()
    {
        var metadata = BuildMetadata("https://a.com", 100, eTag: "etag1");
        MetadataStore.Matches(metadata, "https://b.com", "etag1", null).Should().BeFalse();
    }

    // === Matches:ETag 校验 ===

    [Fact]
    public void Matches_ETagMismatch_ReturnsFalse()
    {
        var metadata = BuildMetadata("https://a.com", 100, eTag: "etag1");
        MetadataStore.Matches(metadata, "https://a.com", "etag2", null).Should().BeFalse();
    }

    [Fact]
    public void Matches_BothETagNull_ReturnsTrue()
    {
        var metadata = BuildMetadata("https://a.com", 100, eTag: null);
        MetadataStore.Matches(metadata, "https://a.com", null, null).Should().BeTrue();
    }

    // === Matches:LastModified 校验 ===

    [Fact]
    public void Matches_LastModifiedMismatch_ReturnsFalse()
    {
        var stored = DateTimeOffset.Parse("2026-08-25T10:00:00Z");
        var current = DateTimeOffset.Parse("2026-08-25T11:00:00Z");
        var metadata = BuildMetadata("https://a.com", 100, lastModified: stored);

        MetadataStore.Matches(metadata, "https://a.com", null, current).Should().BeFalse();
    }

    [Fact]
    public void Matches_LastModifiedWithin1Second_ReturnsTrue()
    {
        var stored = DateTimeOffset.Parse("2026-08-25T10:00:00Z");
        var current = stored.AddMilliseconds(500);
        var metadata = BuildMetadata("https://a.com", 100, lastModified: stored);

        MetadataStore.Matches(metadata, "https://a.com", null, current).Should().BeTrue();
    }

    [Fact]
    public void Matches_LastModifiedOneHasValueOtherNull_ReturnsFalse()
    {
        var stored = DateTimeOffset.Parse("2026-08-25T10:00:00Z");
        var metadata = BuildMetadata("https://a.com", 100, lastModified: stored);

        MetadataStore.Matches(metadata, "https://a.com", null, null).Should().BeFalse();
    }

    // === Matches:全部匹配 ===

    [Fact]
    public void Matches_AllMatch_ReturnsTrue()
    {
        var lm = DateTimeOffset.Parse("2026-08-25T10:00:00Z");
        var metadata = BuildMetadata("https://a.com", 100, eTag: "etag1", lastModified: lm);

        MetadataStore.Matches(metadata, "https://a.com", "etag1", lm).Should().BeTrue();
    }

    // === 辅助 ===

    private static DownloadMetadata BuildMetadata(
        string url,
        long totalLength,
        string? eTag = null,
        DateTimeOffset? lastModified = null) =>
        new()
        {
            Url = url,
            TotalLength = totalLength,
            ETag = eTag,
            LastModified = lastModified
        };
}
