#pragma warning disable JCC3010, JCC3011, JCC3012
namespace State.Tests;

/// <summary>
/// TranscriptFileWriter JSONL 格式测试 — ADR 0144：纯追加写（O(1)，读旧+写回全量已消除）+
/// 逐行流式读（行级容错）+ 存量 .json 数组回退兼容。
/// </summary>
public sealed class TranscriptFileWriterJsonlTests : IAsyncDisposable {
    private readonly TestInMemFs _fs = new();
    private readonly string _tempDir;
    private readonly TranscriptFileWriter _writer;
    private bool _disposed;

    public TranscriptFileWriterJsonlTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"jsonl_test_{Guid.NewGuid():N}");
        _fs.CreateDirectory(_tempDir);
        _writer = new TranscriptFileWriter(_fs, _tempDir);
    }

    /// <summary>两批追加：文件为两行 JSONL，顺序保持，每行独立可反序列化。</summary>
    [Fact]
    public async Task Append_TwoBatches_WritesOneLinePerEntry() {
        var filePath = Path.Combine(_tempDir, "s1", "transcript.jsonl");
        await _writer.AppendEntryAsync(filePath, NewEntry("user", "first"));
        await _writer.AppendEntriesAsync(filePath, [NewEntry("assistant", "second"), NewEntry("user", "third")]);

        var lines = (await _fs.ReadAllTextAsync(filePath)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        var contents = new List<string>(lines.Length);
        foreach (var line in lines) {
            if (RelaxedJsonSerializer.Deserialize(line, TranscriptJsonContext.Default.TranscriptEntry) is { } entry) {
                contents.Add(entry.Content);
            } else {
                Assert.Fail($"JSONL 行反序列化失败: {line[..Math.Min(60, line.Length)]}");
            }
        }

        Assert.Equal(["first", "second", "third"], contents);
    }

    /// <summary>纯追加不重写：首行字节在后续追加后不变（消除读旧数组+写回全量）。</summary>
    [Fact]
    public async Task Append_IsPureAppend_FirstLineUnchanged() {
        var filePath = Path.Combine(_tempDir, "s2", "transcript.jsonl");
        await _writer.AppendEntryAsync(filePath, NewEntry("user", "immutable-first"));
        var firstLine = (await _fs.ReadAllTextAsync(filePath)).Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];

        await _writer.AppendEntryAsync(filePath, NewEntry("user", "second"));
        var lines = (await _fs.ReadAllTextAsync(filePath)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal(firstLine, lines[0]);
    }

    /// <summary>读取 JSONL：逐行解析 + paste-cache 还原路径正常。</summary>
    [Fact]
    public async Task Load_JsonlLines_ParsesEachEntry() {
        var filePath = Path.Combine(_tempDir, "s3", "transcript.jsonl");
        await _writer.AppendEntriesAsync(filePath, [NewEntry("user", "a"), NewEntry("assistant", "b")]);

        var loaded = await _writer.LoadTranscriptAsync(filePath);
        Assert.Equal(["a", "b"], loaded.Select(e => e.Content));
    }

    /// <summary>存量回退：.jsonl 不存在但 .json（JSON 数组）存在 → 回退数组读取（存量会话零迁移可用）。</summary>
    [Fact]
    public async Task Load_FallsBackToLegacyJsonArray_WhenJsonlAbsent() {
        var dir = Path.Combine(_tempDir, "s4");
        _fs.CreateDirectory(dir);
        var legacyPath = Path.Combine(dir, "transcript.json");
        await _fs.WriteAllTextAsync(legacyPath, "[{\"sessionId\":\"s4\",\"role\":\"user\",\"content\":\"legacy\",\"timestamp\":\"2026-01-01T00:00:00Z\"}]");

        var jsonlPath = Path.Combine(dir, "transcript.jsonl");
        var loaded = await _writer.LoadTranscriptAsync(jsonlPath);

        Assert.Single(loaded);
        Assert.Equal("legacy", loaded[0].Content);
    }

    /// <summary>双格式并存：.jsonl 优先（迁移后或新写入的会话）。</summary>
    [Fact]
    public async Task Load_PrefersJsonl_WhenBothExist() {
        var dir = Path.Combine(_tempDir, "s5");
        _fs.CreateDirectory(dir);
        await _fs.WriteAllTextAsync(Path.Combine(dir, "transcript.json"), "[{\"sessionId\":\"s5\",\"role\":\"user\",\"content\":\"old-array\",\"timestamp\":\"2026-01-01T00:00:00Z\"}]");

        var jsonlPath = Path.Combine(dir, "transcript.jsonl");
        await _writer.AppendEntryAsync(jsonlPath, NewEntry("user", "new-jsonl"));

        var loaded = await _writer.LoadTranscriptAsync(jsonlPath);
        Assert.Single(loaded);
        Assert.Equal("new-jsonl", loaded[0].Content);
    }

    /// <summary>行级容错：坏行跳过（JSONL 格式优势 — 数组格式整体失败返回空）。</summary>
    [Fact]
    public async Task Load_SkipsMalformedLines() {
        var dir = Path.Combine(_tempDir, "s6");
        _fs.CreateDirectory(dir);
        var jsonlPath = Path.Combine(dir, "transcript.jsonl");
        await _fs.WriteAllTextAsync(jsonlPath,
            RelaxedJsonSerializer.Serialize(NewEntry("user", "good"), TranscriptJsonlContext.Default) + "\n{broken json\n" +
            RelaxedJsonSerializer.Serialize(NewEntry("assistant", "also-good"), TranscriptJsonlContext.Default) + "\n");

        var loaded = await _writer.LoadTranscriptAsync(jsonlPath);
        Assert.Equal(["good", "also-good"], loaded.Select(e => e.Content));
    }

    /// <summary>文件尾残留空行（写入中断）不影响读取。</summary>
    [Fact]
    public async Task Load_ToleratesTrailingNewlines() {
        var dir = Path.Combine(_tempDir, "s7");
        _fs.CreateDirectory(dir);
        var jsonlPath = Path.Combine(dir, "transcript.jsonl");
        var line = RelaxedJsonSerializer.Serialize(NewEntry("user", "only"), TranscriptJsonlContext.Default);
        await _fs.WriteAllTextAsync(jsonlPath, line + "\n\n");

        var loaded = await _writer.LoadTranscriptAsync(jsonlPath);
        Assert.Single(loaded);
    }

    private static TranscriptEntry NewEntry(string role, string content) => new() {
        SessionId = "s",
        Role = role,
        Content = content,
        Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;
        await _writer.DisposeAsync();
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
