namespace Core.Tests;

public class FileStateGuardNodeTests {
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public void HasBeenRead_NullCache_ReturnsTrue() {
        var node = new FileStateGuardNode(_fs);

        var result = node.HasBeenRead("/test.txt");

        Assert.True(result);
    }

    [Fact]
    public void HasBeenRead_FileNotExists_ReturnsTrue() {
        var cache = new Mock<IFileStateCache>();
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead("/nonexistent/file.txt");

        Assert.True(result);
    }

    [Fact]
    public void HasBeenRead_FileExistsInCache_ReturnsTrue() {
        var filePath = CreateFile("test");
        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.HasBeenRead(filePath)).Returns(true);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead(filePath);

        Assert.True(result);
    }

    [Fact]
    public void HasBeenRead_FileExistsNotInCache_ReturnsFalse() {
        var filePath = CreateFile("test");
        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.HasBeenRead(filePath)).Returns(false);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead(filePath);

        Assert.False(result);
    }

    [Fact]
    public async Task CheckStaleWriteAsync_NullCache_ReturnsNull() {
        var node = new FileStateGuardNode(_fs);

        var result = await node.CheckStaleWriteAsync("/test.txt", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckStaleWriteAsync_FileNotExists_ReturnsNull() {
        var cache = new Mock<IFileStateCache>();
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync("/nonexistent/file.txt", CancellationToken.None);

        Assert.Null(result);
    }

    // === rebase 兜底场景（核心：时间戳变但内容不变 → 放行）===

    /// <summary>
    /// rebase 场景：文件内容不变，但时间戳变新（git rebase/checkout 触碰 mtime）。
    /// 时间戳道拦截 → 回退内容比对 → 内容相同 → 放行。
    /// </summary>
    [Fact]
    public async Task CheckStaleWriteAsync_TimestampNewerButContentSame_ReturnsNull() {
        const string content = "original content unchanged";
        var filePath = CreateFile(content);
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadTimestampMs(filePath)).Returns(lastWriteMs - 10_000);
        cache.Setup(c => c.GetReadContent(filePath)).Returns(content);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>
    /// rebase 场景：其他分支改了这文件 → 时间戳变新且内容变 → 拒绝，返回诊断元组。
    /// </summary>
    [Fact]
    public async Task CheckStaleWriteAsync_TimestampNewerAndContentChanged_ReturnsStale() {
        const string readContent = "AI read this";
        const string currentContent = "rebase changed this";
        var filePath = CreateFile(currentContent);
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadTimestampMs(filePath)).Returns(lastWriteMs - 10_000);
        cache.Setup(c => c.GetReadContent(filePath)).Returns(readContent);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(lastWriteMs, result!.Value.LastWriteMs);
        Assert.Equal(lastWriteMs - 10_000, result.Value.ReadTimestampMs);
    }

    /// <summary>
    /// 时间戳在 1s 容忍内 → 直接放行，不进兜底。
    /// </summary>
    [Fact]
    public async Task CheckStaleWriteAsync_TimestampWithinTolerance_ReturnsNull() {
        const string content = "content";
        var filePath = CreateFile(content);
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadTimestampMs(filePath)).Returns(lastWriteMs);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.Null(result);
    }

    // === 边缘场景 ===

    /// <summary>
    /// 边缘场景：AI 只读了文件部分视图（offset/limit），rebase 后全量内容与部分内容比对不等 → 拒绝。
    /// 保守安全策略：部分读无法确认全量未变，强制重读。rebase 内容没变也会误拒，但安全优先。
    /// </summary>
    [Fact]
    public async Task CheckStaleWriteAsync_PartialRead_ContentMismatch_ReturnsStale() {
        const string fullContent = "line1\nline2\nline3\nline4\nline5";
        const string partialContent = "line2\nline3";
        var filePath = CreateFile(fullContent);
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadTimestampMs(filePath)).Returns(lastWriteMs - 10_000);
        cache.Setup(c => c.GetReadContent(filePath)).Returns(partialContent);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.NotNull(result);
    }

    /// <summary>
    /// 边缘场景：缓存被 LRU 淘汰，GetReadTimestampMs 返回 null → 直接放行。
    /// 已知取舍：大文件被淘汰后脏写保护失效，依赖 RequireReadBeforeWrite 守卫兜底。
    /// </summary>
    [Fact]
    public async Task CheckStaleWriteAsync_ReadTimestampNull_ReturnsNull() {
        var filePath = CreateFile("content");

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadTimestampMs(filePath)).Returns((long?)null);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>
    /// 边缘场景：有 readTimestamp 但 readContent 为 null（防御性：理论上不应发生）。
    /// 无法做内容兜底 → 保守拒绝。
    /// </summary>
    [Fact]
    public async Task CheckStaleWriteAsync_ReadContentNullButTimestampPresent_ReturnsStale() {
        var filePath = CreateFile("content");
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadTimestampMs(filePath)).Returns(lastWriteMs - 10_000);
        cache.Setup(c => c.GetReadContent(filePath)).Returns((string?)null);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.NotNull(result);
    }

    /// <summary>
    /// 边缘场景：空文件 rebase，content="" → 兜底比对空字符串相等 → 放行。
    /// </summary>
    [Fact]
    public async Task CheckStaleWriteAsync_EmptyContent_TimestampNewer_ReturnsNull() {
        var filePath = CreateFile("");
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadTimestampMs(filePath)).Returns(lastWriteMs - 10_000);
        cache.Setup(c => c.GetReadContent(filePath)).Returns("");
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.Null(result);
    }

    private string CreateFile(string content) {
        var path = Path.Combine(Path.GetTempPath(), $"state_test_{Guid.NewGuid():N}.txt");
        _fs.WriteAllText(path, content).GetAwaiter().GetResult();
        return path;
    }
}