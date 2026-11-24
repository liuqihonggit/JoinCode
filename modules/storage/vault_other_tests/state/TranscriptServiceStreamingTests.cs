#pragma warning disable JCC3010, JCC3011, JCC3012
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace State.Tests;

/// <summary>
/// 流式旁路落盘（ADR 0166）— transcript 只在回合结束后落盘，打断/崩溃会丢掉整段已生成正文。
/// 旁路文件存「累积全文快照」，读端 <c>LoadTranscriptAsync</c> 收编最后一行；主文件已含同一段内容时不重复。
/// </summary>
public sealed class TranscriptServiceStreamingTests : IDisposable {
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly TranscriptService _service;
    private bool _disposed;

    public TranscriptServiceStreamingTests() {
        _service = new TranscriptService(_fs, "/test/streaming-transcript/");
    }

    /// <summary>构造一条消息记录。</summary>
    private static TranscriptEntry NewEntry(string role, string content, string sessionId = "session-1") =>
        new() { Role = role, Content = content, SessionId = sessionId, Timestamp = DateTime.UtcNow };

    /// <summary>旁路快照写入 → 读端收编最后一行（累积全文，最全的那条）。</summary>
    [Fact]
    public async Task LoadTranscriptAsync_AdoptsLastStreamingSnapshot() {
        await _service.AppendEntryAsync("s-stream", NewEntry("user", "问题", "s-stream")).ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-stream", null, null, "残稿第一段").ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-stream", null, null, "残稿第一段 + 第二段").ConfigureAwait(true);

        var entries = await _service.LoadTranscriptAsync("s-stream").ConfigureAwait(true);

        Assert.Equal(2, entries.Count);
        Assert.Equal("问题", entries[0].Content);
        Assert.Equal("残稿第一段 + 第二段", entries[1].Content);
        Assert.Equal(TranscriptService.StreamingEntryType, entries[1].Type);
    }

    /// <summary>主 transcript 已含同一段内容（回合正常落盘后残留的旁路）→ 不重复追加。</summary>
    [Fact]
    public async Task LoadTranscriptAsync_WhenMainAlreadyContains_DoesNotDuplicate() {
        await _service.AppendEntryAsync("s-dup", NewEntry("user", "问题", "s-dup")).ConfigureAwait(true);
        await _service.AppendEntryAsync("s-dup", NewEntry("assistant", "完整回复内容", "s-dup")).ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-dup", null, null, "完整回复内容").ConfigureAwait(true);

        var entries = await _service.LoadTranscriptAsync("s-dup").ConfigureAwait(true);

        Assert.Equal(2, entries.Count);
        Assert.Single(entries, e => e.Content == "完整回复内容");
    }

    /// <summary>清理旁路后不再收编（回合正常落盘路径的收尾）。</summary>
    [Fact]
    public async Task ClearStreamingAsync_ThenLoad_DoesNotAdopt() {
        await _service.AppendEntryAsync("s-clear", NewEntry("user", "问题", "s-clear")).ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-clear", null, null, "残稿").ConfigureAwait(true);
        await _service.ClearStreamingAsync("s-clear", null).ConfigureAwait(true);

        var entries = await _service.LoadTranscriptAsync("s-clear").ConfigureAwait(true);

        Assert.Single(entries);
        Assert.DoesNotContain(entries, e => e.Content == "残稿");
    }

    /// <summary>空内容不落旁路（避免 0 字符残稿把空行收编成幽灵消息）。</summary>
    [Fact]
    public async Task AppendStreamingSnapshotAsync_EmptyContent_WritesNothing() {
        await _service.AppendStreamingSnapshotAsync("s-empty", null, null, string.Empty).ConfigureAwait(true);

        Assert.False(_fs.FileExists("/test/streaming-transcript/s-empty/streaming.jsonl"));
    }

    /// <summary>清理不存在的旁路是 no-op（幂等）。</summary>
    [Fact]
    public async Task ClearStreamingAsync_WithoutFile_IsNoOp() {
        await _service.ClearStreamingAsync("s-none", "q-none").ConfigureAwait(true);

        Assert.False(_fs.FileExists("/test/streaming-transcript/s-none/streaming.q-none.jsonl"));
    }

    // === TASK135 A1 — 旁路按查询归属（并发查询常态下不再互删/串台） ===

    /// <summary>两条并发查询各写各的旁路文件，后写者不覆盖前者。</summary>
    [Fact]
    public async Task AppendStreamingSnapshotAsync_DifferentQueries_WriteSeparateFiles() {
        await _service.AppendStreamingSnapshotAsync("s-multi", "q-a", null, "查询 A 的正文").ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-multi", "q-b", null, "查询 B 的正文").ConfigureAwait(true);

        Assert.True(_fs.FileExists("/test/streaming-transcript/s-multi/streaming.q-a.jsonl"));
        Assert.True(_fs.FileExists("/test/streaming-transcript/s-multi/streaming.q-b.jsonl"));
    }

    /// <summary>一条查询正常收尾清理旁路，不得删掉仍在运行的另一条查询的残稿。</summary>
    [Fact]
    public async Task ClearStreamingAsync_OnlyClearsOwnQuery() {
        await _service.AppendStreamingSnapshotAsync("s-iso", "q-done", null, "已完成的查询").ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-iso", "q-live", null, "仍在流式的查询").ConfigureAwait(true);

        await _service.ClearStreamingAsync("s-iso", "q-done").ConfigureAwait(true);

        Assert.False(_fs.FileExists("/test/streaming-transcript/s-iso/streaming.q-done.jsonl"));
        Assert.True(_fs.FileExists("/test/streaming-transcript/s-iso/streaming.q-live.jsonl"),
            "清理一条查询的旁路不得影响另一条在途查询（ADR 0165 前台+后台并行是常态）");
    }

    /// <summary>收编时按查询分组：两条残稿各自成条，不互相覆盖（A 崩溃、B 正常时不把 A 的内容挂到 B 名下）。</summary>
    [Fact]
    public async Task LoadTranscriptAsync_AdoptsEachQuerySnapshotSeparately() {
        await _service.AppendEntryAsync("s-group", NewEntry("user", "同一个会话的两个问题", "s-group")).ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-group", "q-a", null, "A 的残稿").ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-group", "q-b", null, "B 的残稿").ConfigureAwait(true);

        var entries = await _service.LoadTranscriptAsync("s-group").ConfigureAwait(true);

        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Content == "A 的残稿");
        Assert.Contains(entries, e => e.Content == "B 的残稿");
    }

    // === TASK135 A3 — 收编是一次性动作（落盘 + 删旁路，重复加载不重复出现） ===

    /// <summary>收编后残稿进入主 transcript 并删除旁路文件。</summary>
    [Fact]
    public async Task LoadTranscriptAsync_AdoptionPersistsToMainAndRemovesSidecar() {
        await _service.AppendEntryAsync("s-once", NewEntry("user", "问题", "s-once")).ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-once", "q-once", null, "被打断的回复").ConfigureAwait(true);

        await _service.LoadTranscriptAsync("s-once").ConfigureAwait(true);

        Assert.False(_fs.FileExists("/test/streaming-transcript/s-once/streaming.q-once.jsonl"),
            "收编即消费 — 旁路必须删除，否则每次加载都重复收编同一条残稿");
        var reloaded = await _service.LoadTranscriptAsync("s-once").ConfigureAwait(true);
        Assert.Single(reloaded, e => e.Content == "被打断的回复");
    }

    /// <summary>连续两次加载，残稿只出现一次（幂等）。</summary>
    [Fact]
    public async Task LoadTranscriptAsync_Twice_AdoptsOnce() {
        await _service.AppendEntryAsync("s-idem", NewEntry("user", "问题", "s-idem")).ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-idem", "q-idem", null, "只该出现一次").ConfigureAwait(true);

        var first = await _service.LoadTranscriptAsync("s-idem").ConfigureAwait(true);
        var second = await _service.LoadTranscriptAsync("s-idem").ConfigureAwait(true);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        _service.DisposeSafe();
    }
}
