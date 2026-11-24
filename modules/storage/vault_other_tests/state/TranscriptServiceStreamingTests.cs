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
        await _service.AppendStreamingSnapshotAsync("s-stream", "残稿第一段").ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-stream", "残稿第一段 + 第二段").ConfigureAwait(true);

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
        await _service.AppendStreamingSnapshotAsync("s-dup", "完整回复内容").ConfigureAwait(true);

        var entries = await _service.LoadTranscriptAsync("s-dup").ConfigureAwait(true);

        Assert.Equal(2, entries.Count);
        Assert.Single(entries, e => e.Content == "完整回复内容");
    }

    /// <summary>清理旁路后不再收编（回合正常落盘路径的收尾）。</summary>
    [Fact]
    public async Task ClearStreamingAsync_ThenLoad_DoesNotAdopt() {
        await _service.AppendEntryAsync("s-clear", NewEntry("user", "问题", "s-clear")).ConfigureAwait(true);
        await _service.AppendStreamingSnapshotAsync("s-clear", "残稿").ConfigureAwait(true);
        await _service.ClearStreamingAsync("s-clear").ConfigureAwait(true);

        var entries = await _service.LoadTranscriptAsync("s-clear").ConfigureAwait(true);

        Assert.Single(entries);
        Assert.DoesNotContain(entries, e => e.Content == "残稿");
    }

    /// <summary>空内容不落旁路（避免 0 字符残稿把空行收编成幽灵消息）。</summary>
    [Fact]
    public async Task AppendStreamingSnapshotAsync_EmptyContent_WritesNothing() {
        await _service.AppendStreamingSnapshotAsync("s-empty", string.Empty).ConfigureAwait(true);

        Assert.False(_fs.FileExists("/test/streaming-transcript/s-empty/streaming.jsonl"));
    }

    /// <summary>清理不存在的旁路是 no-op（幂等）。</summary>
    [Fact]
    public async Task ClearStreamingAsync_WithoutFile_IsNoOp() {
        await _service.ClearStreamingAsync("s-none").ConfigureAwait(true);

        Assert.False(_fs.FileExists("/test/streaming-transcript/s-none/streaming.jsonl"));
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        _service.DisposeSafe();
    }
}
