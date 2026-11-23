// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Core.Tests;

public class FileStateGuardNodeTests {
    private readonly IFileSystem _fs = TestFileSystem.Current;

    // === HasBeenRead ===

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
    public void HasBeenRead_FullRead_ReturnsTrue() {
        var filePath = CreateFile("test");
        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadState(filePath)).Returns(FullReadState("test", 0));
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead(filePath);

        Assert.True(result);
    }

    [Fact]
    public void HasBeenRead_NotInCache_ReturnsFalse() {
        var filePath = CreateFile("test");
        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadState(filePath)).Returns((FileReadState?)null);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead(filePath);

        Assert.False(result);
    }

    /// <summary>
    /// 自动注入文件（isPartialView=true）视为未读，对齐 TS FileWriteTool.ts L199 / FileEditTool.ts L276。
    /// TS 中只有自动注入场景（attachments.ts L1749）才设置 isPartialView=true。
    /// </summary>
    [Fact]
    public void HasBeenRead_AutoInjectedPartialView_ReturnsFalse() {
        var filePath = CreateFile("test");
        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadState(filePath)).Returns(PartialReadState("partial", 0));
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead(filePath);

        Assert.False(result);
    }

    /// <summary>
    /// 普通 Read 带 offset/limit 但 IsPartialView=false → 视为已读，对齐 TS FileEditTool.ts L276。
    /// TS 中普通 Read 带 offset/limit 不设置 isPartialView，允许后续 edit。
    /// </summary>
    [Fact]
    public void HasBeenRead_PartialReadWithoutIsPartialView_ReturnsTrue() {
        var filePath = CreateFile("test");
        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadState(filePath)).Returns(new FileReadState {
            Content = "partial content",
            TimestampMs = 0,
            Offset = 2,
            Limit = 2,
            IsPartialView = false,
        });
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead(filePath);

        Assert.True(result);
    }

    // === CheckStaleWriteAsync 短路 ===

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
        cache.Setup(c => c.GetReadState(filePath)).Returns(FullReadState(content, lastWriteMs - 10_000));
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>
    /// rebase 场景：其他分支改了这文件 → 时间戳变新且内容变 → 拒绝，返回诊断元组。
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task CheckStaleWriteAsync_TimestampNewerAndContentChanged_ReturnsStale() {
        const string readContent = "AI read this";
        const string currentContent = "rebase changed this";
        var filePath = CreateFile(currentContent);
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadState(filePath)).Returns(FullReadState(readContent, lastWriteMs - 10_000));
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(lastWriteMs, result!.LastWriteMs);
        Assert.Equal(lastWriteMs - 10_000, result.ReadTimestampMs);
        Assert.False(result.IsNotRead);
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
        cache.Setup(c => c.GetReadState(filePath)).Returns(FullReadState(content, lastWriteMs));
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.Null(result);
    }

    // === 边缘场景 ===

    /// <summary>
    /// 边缘场景：部分读（有 offset/limit，IsPartialView=false）+ 时间戳变 → 拒绝，不进内容兜底。
    /// 对齐 TS FileEditTool.ts L296-301: isFullRead = offset===undefined && limit===undefined，
    /// 部分读不走内容兜底（缓存内容不是全量），保守拒绝。
    /// </summary>
    [Fact]
    public async Task CheckStaleWriteAsync_PartialReadByOffsetLimit_TimestampNewer_ReturnsStale() {
        const string fullContent = "line1\nline2\nline3\nline4\nline5";
        const string partialContent = "line2\nline3";
        var filePath = CreateFile(fullContent);
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadState(filePath)).Returns(new FileReadState {
            Content = partialContent,
            TimestampMs = lastWriteMs - 10_000,
            Offset = 2,
            Limit = 2,
            IsPartialView = false,
        });
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.NotNull(result);
    }

    /// <summary>
    /// 边缘场景：缓存无记录（LRU 淘汰）→ 防御性拒绝（IsNotRead=true），对齐 TS FileWriteTool.ts L282。
    /// 实际守卫链中 RequireReadBeforeWrite 会先拦下，此处为防御性兜底。
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task CheckStaleWriteAsync_ReadStateNull_ReturnsStaleNotRead() {
        var filePath = CreateFile("content");

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadState(filePath)).Returns((FileReadState?)null);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsNotRead);
    }

    /// <summary>
    /// 边缘场景：空文件 rebase，content="" → 兜底比对空字符串相等 → 放行。
    /// </summary>
    [Fact]
    public async Task CheckStaleWriteAsync_EmptyContent_TimestampNewer_ReturnsNull() {
        var filePath = CreateFile("");
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.GetReadState(filePath)).Returns(FullReadState("", lastWriteMs - 10_000));
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync(filePath, CancellationToken.None);

        Assert.Null(result);
    }

    // === helpers ===

    private static FileReadState FullReadState(string content, long timestampMs) => new() {
        Content = content,
        TimestampMs = timestampMs,
        Offset = null,
        Limit = null,
        IsPartialView = false,
    };

    private static FileReadState PartialReadState(string content, long timestampMs) => new() {
        Content = content,
        TimestampMs = timestampMs,
        Offset = 2,
        Limit = 2,
        IsPartialView = true,
    };

    private string CreateFile(string content) {
        var path = Path.Combine(Path.GetTempPath(), $"state_test_{Guid.NewGuid():N}.txt");
        _fs.WriteAllText(path, content).GetAwaiter().GetResult();
        return path;
    }
}
