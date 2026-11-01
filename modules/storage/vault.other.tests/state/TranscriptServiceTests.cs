#pragma warning disable JCC3010, JCC3011, JCC3012
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace State.Tests;

public sealed class TranscriptServiceTests : IDisposable {
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly TranscriptService _service;
    private bool _disposed;

    public TranscriptServiceTests() {
        _service = new TranscriptService(_fs, "/test/transcript/");
    }

    [Fact]
    public async Task AppendEntryAsync_Should_Create_Jsonl_File() {
        var entry = NewEntry("user", "Hello world");

        await _service.AppendEntryAsync("session-1", entry).ConfigureAwait(true);

        var filePath = "/test/transcript/session-1/transcript.jsonl";
        Assert.True(_fs.FileExists(filePath));

        var json = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("Hello world", json);
    }

    [Fact]
    public async Task AppendEntryAsync_Should_Override_SessionId() {
        var entry = NewEntry("user", "test", sessionId: "wrong");

        await _service.AppendEntryAsync("correct-session", entry).ConfigureAwait(true);

        var transcript = await _service.LoadTranscriptAsync("correct-session").ConfigureAwait(true);
        Assert.Single(transcript);
        Assert.Equal("correct-session", transcript[0].SessionId);
    }

    [Fact]
    public async Task AppendEntriesAsync_Should_Write_Multiple_Lines() {
        var entries = new List<TranscriptEntry>
        {
            NewEntry("user", "Hello"),
            NewEntry("assistant", "Hi there"),
            NewEntry("user", "How are you?")
        };

        await _service.AppendEntriesAsync("session-2", entries).ConfigureAwait(true);

        var transcript = await _service.LoadTranscriptAsync("session-2").ConfigureAwait(true);
        Assert.Equal(3, transcript.Count);
        Assert.Equal("Hello", transcript[0].Content);
        Assert.Equal("Hi there", transcript[1].Content);
        Assert.Equal("How are you?", transcript[2].Content);
    }

    [Fact]
    public async Task AppendEntriesAsync_With_Empty_List_Should_Not_Create_File() {
        await _service.AppendEntriesAsync("session-empty", []).ConfigureAwait(true);

        Assert.False(await _service.TranscriptExistsAsync("session-empty").ConfigureAwait(true));
    }

    [Fact]
    public async Task LoadTranscriptAsync_Should_Return_Empty_For_Nonexistent_Session() {
        var transcript = await _service.LoadTranscriptAsync("nonexistent").ConfigureAwait(true);

        Assert.Empty(transcript);
    }

    [Fact]
    public async Task LoadTranscriptAsync_Should_Read_Valid_Jsonl_Lines() {
        var filePath = "/test/transcript/malformed/transcript.jsonl";
        _fs.CreateDirectory("/test/transcript/malformed");
        var line = RelaxedJsonSerializer.Serialize(new TranscriptEntry { SessionId = "malformed", Role = "user", Content = "ok", Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, TranscriptJsonlContext.Default);
        await _fs.WriteAllTextAsync(filePath, line + "\n").ConfigureAwait(true);

        var transcript = await _service.LoadTranscriptAsync("malformed").ConfigureAwait(true);

        Assert.Single(transcript);
        Assert.Equal("ok", transcript[0].Content);
    }

    /// <summary>存量兼容：仅存在旧 .json(JSON 数组)的会话仍可读取（零迁移可用）。</summary>
    [Fact]
    public async Task LoadTranscriptAsync_Should_Read_Legacy_Json_Array() {
        var filePath = "/test/transcript/legacy-array/transcript.json";
        _fs.CreateDirectory("/test/transcript/legacy-array");
        var json = "[{\"sessionId\":\"legacy-array\",\"role\":\"user\",\"content\":\"legacy-ok\",\"timestamp\":\"2025-01-01T00:00:00Z\"}]";
        await _fs.WriteAllTextAsync(filePath, json).ConfigureAwait(true);

        var transcript = await _service.LoadTranscriptAsync("legacy-array").ConfigureAwait(true);

        Assert.Single(transcript);
        Assert.Equal("legacy-ok", transcript[0].Content);
    }

    [Fact]
    public async Task ListTranscriptsAsync_Should_Return_Summaries() {
        await _service.AppendEntriesAsync("list-1", [NewEntry("user", "First")]).ConfigureAwait(true);
        await _service.AppendEntriesAsync("list-2", [NewEntry("user", "Second"), NewEntry("assistant", "Reply")]).ConfigureAwait(true);

        var summaries = await _service.ListTranscriptsAsync().ConfigureAwait(true);

        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.SessionId == "list-1" && s.MessageCount == 1);
        Assert.Contains(summaries, s => s.SessionId == "list-2" && s.MessageCount == 2);
    }

    [Fact]
    public async Task ListTranscriptsAsync_Should_Respect_Limit() {
        for (var i = 0; i < 5; i++) {
            await _service.AppendEntryAsync($"limit-{i}", NewEntry("user", $"msg {i}")).ConfigureAwait(true);
        }

        var summaries = await _service.ListTranscriptsAsync(limit: 3).ConfigureAwait(true);

        Assert.Equal(3, summaries.Count);
    }

    [Fact]
    public async Task ListTranscriptsAsync_Should_Be_Ordered_By_LastModified() {
        await _service.AppendEntryAsync("older", NewEntry("user", "old")).ConfigureAwait(true);

        // 等待时间戳变化 - 使用 SpinWait 替代 Task.Delay 反模式
        var olderTime = _fs.GetLastWriteTimeUtc("/test/transcript/older/transcript.jsonl");
        SpinWait.SpinUntil(() => DateTime.UtcNow > olderTime, TimeSpan.FromMilliseconds(100));

        await _service.AppendEntryAsync("newer", NewEntry("user", "new")).ConfigureAwait(true);

        var summaries = await _service.ListTranscriptsAsync().ConfigureAwait(true);

        Assert.Equal("newer", summaries[0].SessionId);
        Assert.Equal("older", summaries[1].SessionId);
    }

    [Fact]
    public async Task DeleteTranscriptAsync_Should_Remove_File() {
        await _service.AppendEntryAsync("to-delete", NewEntry("user", "bye")).ConfigureAwait(true);

        Assert.True(await _service.TranscriptExistsAsync("to-delete").ConfigureAwait(true));

        var deleted = await _service.DeleteTranscriptAsync("to-delete").ConfigureAwait(true);

        Assert.True(deleted);
        Assert.False(await _service.TranscriptExistsAsync("to-delete").ConfigureAwait(true));
    }

    [Fact]
    public async Task DeleteTranscriptAsync_Should_Return_False_For_Nonexistent() {
        var deleted = await _service.DeleteTranscriptAsync("nonexistent").ConfigureAwait(true);

        Assert.False(deleted);
    }

    [Fact]
    public async Task TranscriptExistsAsync_Should_Return_Correct_Status() {
        Assert.False(await _service.TranscriptExistsAsync("no-such-session").ConfigureAwait(true));

        await _service.AppendEntryAsync("exists", NewEntry("user", "hi")).ConfigureAwait(true);

        Assert.True(await _service.TranscriptExistsAsync("exists").ConfigureAwait(true));
    }

    [Fact]
    public async Task AppendEntryAsync_Should_Reject_Invalid_SessionId() {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AppendEntryAsync("../evil", NewEntry("user", "hack"))).ConfigureAwait(true);
    }

    [Fact]
    public async Task AppendEntryAsync_Should_Append_To_Existing_File() {
        await _service.AppendEntryAsync("append-test", NewEntry("user", "first")).ConfigureAwait(true);
        await _service.AppendEntryAsync("append-test", NewEntry("assistant", "second")).ConfigureAwait(true);

        var transcript = await _service.LoadTranscriptAsync("append-test").ConfigureAwait(true);

        Assert.Equal(2, transcript.Count);
        Assert.Equal("first", transcript[0].Content);
        Assert.Equal("second", transcript[1].Content);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task TranscriptSummary_Should_Contain_Preview() {
        await _service.AppendEntryAsync("preview-test", NewEntry("assistant", "This is a long response that should be truncated in the preview")).ConfigureAwait(true);

        var summaries = await _service.ListTranscriptsAsync().ConfigureAwait(true);

        Assert.Single(summaries);
        Assert.NotNull(summaries[0].LastMessagePreview);
        Assert.True(summaries[0].LastMessagePreview!.Length <= 84);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task SaveSessionInfoAsync_Should_Create_Meta_File_And_Roundtrip() {
        var info = new SessionInfo { Id = "meta-1", ProjectPath = "/proj", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

        await _service.SaveSessionInfoAsync("meta-1", info).ConfigureAwait(true);

        var loaded = await _service.GetSessionInfoAsync("meta-1").ConfigureAwait(true);
        Assert.NotNull(loaded);
        Assert.Equal("meta-1", loaded!.Id);
        Assert.Equal("/proj", loaded.ProjectPath);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), loaded.CreatedAt);
    }

    /// <summary>
    /// 复现 bug: SaveSessionInfoAsync 只写 meta.json 不建 transcript.jsonl，
    /// ListTranscriptsAsync 跳过无 transcript 文件的目录 → -c 找不到 -p 的会话。
    /// 修复: CliSession.InitializeAsync 应改用 EnsureSessionAsync。
    /// </summary>
    [Fact]
    public async Task SaveSessionInfoOnly_NoTranscript_ListTranscriptsSkipsIt() {
        var info = new SessionInfo { Id = "saveonly-1", ProjectPath = "/proj/saveonly", CreatedAt = DateTime.UtcNow };

        await _service.SaveSessionInfoAsync("saveonly-1", info).ConfigureAwait(true);

        Assert.True(_fs.FileExists("/test/transcript/saveonly-1/meta.json"));
        Assert.False(_fs.FileExists("/test/transcript/saveonly-1/transcript.jsonl"));
        var summaries = await _service.ListTranscriptsAsync().ConfigureAwait(true);
        Assert.DoesNotContain(summaries, s => s.SessionId == "saveonly-1");
    }

    [Fact]
    public async Task GetSessionInfoAsync_Should_Return_Null_For_Nonexistent() {
        var info = await _service.GetSessionInfoAsync("no-meta-session").ConfigureAwait(true);
        Assert.Null(info);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 断言保证非空")]
    [Fact]
    public async Task EnsureSessionAsync_Should_Create_Meta_And_Empty_Transcript() {
        var info = new SessionInfo {
            Id = "ensure-1",
            ProjectPath = "/proj/ensure",
            Vendor = "deepseek",
            ModelId = "deepseek-chat",
            CreatedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
        };

        var created = await _service.EnsureSessionAsync("ensure-1", info).ConfigureAwait(true);

        Assert.True(created);
        Assert.True(_fs.FileExists("/test/transcript/ensure-1/transcript.jsonl"));
        Assert.Empty(await _service.LoadTranscriptAsync("ensure-1").ConfigureAwait(true));
        var loaded = await _service.GetSessionInfoAsync("ensure-1").ConfigureAwait(true);
        Assert.NotNull(loaded);
        Assert.Equal("/proj/ensure", loaded!.ProjectPath);
        var summary = Assert.Single(await _service.ListTranscriptsAsync().ConfigureAwait(true));
        Assert.Equal("ensure-1", summary.SessionId);
        Assert.Equal(0, summary.MessageCount);
    }

    [Fact]
    public async Task EnsureSessionAsync_Should_Be_Idempotent() {
        await _service.AppendEntryAsync("ensure-2", NewEntry("user", "existing")).ConfigureAwait(true);

        var created = await _service.EnsureSessionAsync("ensure-2", new SessionInfo { Id = "ensure-2", ProjectPath = "/other" }).ConfigureAwait(true);

        Assert.False(created);
        var transcript = await _service.LoadTranscriptAsync("ensure-2").ConfigureAwait(true);
        Assert.Single(transcript);
        Assert.Equal("existing", transcript[0].Content);
    }

    [Fact]
    public async Task DeleteTranscriptAsync_Should_Remove_Session_Directory() {
        await _service.AppendEntryAsync("dir-delete", NewEntry("user", "bye")).ConfigureAwait(true);
        await _service.SaveSessionInfoAsync("dir-delete", new SessionInfo { Id = "dir-delete", ProjectPath = "/p", CreatedAt = DateTime.UtcNow }).ConfigureAwait(true);

        Assert.True(await _service.TranscriptExistsAsync("dir-delete").ConfigureAwait(true));

        var deleted = await _service.DeleteTranscriptAsync("dir-delete").ConfigureAwait(true);

        Assert.True(deleted);
        Assert.False(await _service.TranscriptExistsAsync("dir-delete").ConfigureAwait(true));
        Assert.Null(await _service.GetSessionInfoAsync("dir-delete").ConfigureAwait(true));
    }

    /// <summary>迁移源1：旧扁平 .json(JSONL 行格式) → {id}/transcript.jsonl。</summary>
    [Fact]
    public async Task MigrateLegacyAsync_Should_Move_Flat_Jsonl_To_SessionDirectory() {
        var flatPath = "/test/transcript/legacy-migrate-1.json";
        await _fs.WriteAllTextAsync(flatPath, "{\"sessionId\":\"legacy-migrate-1\",\"role\":\"user\",\"content\":\"old-data\",\"timestamp\":\"2025-01-01T00:00:00Z\"}\n").ConfigureAwait(true);

        await _service.MigrateLegacyAsync().ConfigureAwait(true);

        var newPath = "/test/transcript/legacy-migrate-1/transcript.jsonl";
        Assert.True(_fs.FileExists(newPath));
        var content = await _fs.ReadAllTextAsync(newPath).ConfigureAwait(true);
        Assert.Contains("old-data", content);
        // 迁移后可正常读取（含写入时 SessionId 等字段）
        var loaded = await _service.LoadTranscriptAsync("legacy-migrate-1").ConfigureAwait(true);
        Assert.Single(loaded);
        Assert.Equal("old-data", loaded[0].Content);
    }

    /// <summary>迁移源2：子目录 transcript.json(JSON 数组) → 同目录 transcript.jsonl（ADR 0144 数组→JSONL）。</summary>
    [Fact]
    public async Task MigrateLegacyAsync_Should_Convert_SessionJsonArray_To_Jsonl() {
        _fs.CreateDirectory("/test/transcript/array-migrate");
        await _fs.WriteAllTextAsync("/test/transcript/array-migrate/transcript.json",
            "[{\"sessionId\":\"array-migrate\",\"role\":\"user\",\"content\":\"array-entry\",\"timestamp\":\"2025-06-01T00:00:00Z\"},{\"sessionId\":\"array-migrate\",\"role\":\"assistant\",\"content\":\"reply\",\"timestamp\":\"2025-06-01T00:00:01Z\"}]").ConfigureAwait(true);

        await _service.MigrateLegacyAsync().ConfigureAwait(true);

        var jsonlPath = "/test/transcript/array-migrate/transcript.jsonl";
        Assert.True(_fs.FileExists(jsonlPath));
        var loaded = await _service.LoadTranscriptAsync("array-migrate").ConfigureAwait(true);
        Assert.Equal(["array-entry", "reply"], loaded.Select(e => e.Content));
        // 幂等：旧 .json 保留（不删旧文件红线）
        Assert.True(_fs.FileExists("/test/transcript/array-migrate/transcript.json"));
    }

    [Fact]
    public async Task MigrateLegacyAsync_Should_Be_Idempotent() {
        var flatPath = "/test/transcript/legacy-migrate-2.json";
        await _fs.WriteAllTextAsync(flatPath, "{\"sessionId\":\"legacy-migrate-2\",\"role\":\"user\",\"content\":\"data\",\"timestamp\":\"2025-01-01T00:00:00Z\"}\n").ConfigureAwait(true);

        await _service.MigrateLegacyAsync().ConfigureAwait(true);
        await _service.MigrateLegacyAsync().ConfigureAwait(true);

        var newPath = "/test/transcript/legacy-migrate-2/transcript.jsonl";
        Assert.True(_fs.FileExists(newPath));
        // 幂等：重复迁移不产生重复条目
        var loaded = await _service.LoadTranscriptAsync("legacy-migrate-2").ConfigureAwait(true);
        Assert.Single(loaded);
    }

    private static TranscriptEntry NewEntry(string role, string content, string sessionId = "test") {
        return new TranscriptEntry {
            SessionId = sessionId,
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow
        };
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        _service.DisposeSafe();
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012