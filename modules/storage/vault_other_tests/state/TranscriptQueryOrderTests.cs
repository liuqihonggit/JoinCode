#pragma warning disable JCC3010, JCC3011, JCC3012
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace State.Tests;

/// <summary>
/// TASK135 A2 — 并发查询的 transcript 落盘顺序是「完成顺序」，不等于「提问顺序」。
/// 条目带 <c>TurnSeq</c>（提交时分配）后，读端必须按它稳定排序，否则 resume 上下文倒序。
/// </summary>
public sealed class TranscriptQueryOrderTests : IDisposable {
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly TranscriptService _service;
    private bool _disposed;

    public TranscriptQueryOrderTests() {
        _service = new TranscriptService(_fs, "/test/transcript-order/");
    }

    private static TranscriptEntry Entry(string role, string content, int? turnSeq, string? queryId = null) =>
        new() {
            Role = role,
            Content = content,
            SessionId = "s-order",
            TurnSeq = turnSeq,
            QueryId = queryId,
            Timestamp = DateTime.UtcNow,
        };

    /// <summary>后提交的查询先落盘 → 读回必须按 TurnSeq 还原提问顺序。</summary>
    [Fact]
    public async Task LoadTranscriptAsync_OrdersByTurnSeq_NotAppendOrder() {
        // 第 2 回合先完成落盘（并发后台查询的常态）
        await _service.AppendEntriesAsync("s-order", [
            Entry("user", "第二个问题", 2, "q-second"),
            Entry("assistant", "第二个回答", 2, "q-second"),
        ]).ConfigureAwait(true);
        // 第 1 回合后完成
        await _service.AppendEntriesAsync("s-order", [
            Entry("user", "第一个问题", 1, "q-first"),
            Entry("assistant", "第一个回答", 1, "q-first"),
        ]).ConfigureAwait(true);

        var entries = await _service.LoadTranscriptAsync("s-order").ConfigureAwait(true);

        Assert.Equal(["第一个问题", "第一个回答", "第二个问题", "第二个回答"],
            entries.Select(e => e.Content).ToArray());
    }

    /// <summary>同一回合的多条记录（user + assistant）共享 TurnSeq，保持落盘时的内部顺序。</summary>
    [Fact]
    public async Task LoadTranscriptAsync_SameTurnSeq_KeepsRelativeOrder() {
        await _service.AppendEntriesAsync("s-order", [
            Entry("user", "问题 A", 1),
            Entry("assistant", "回答 A", 1),
        ]).ConfigureAwait(true);

        var entries = await _service.LoadTranscriptAsync("s-order").ConfigureAwait(true);

        Assert.Equal(["问题 A", "回答 A"], entries.Select(e => e.Content).ToArray());
    }

    /// <summary>旧记录没有 TurnSeq — 排在有号记录之前，且彼此保持原有相对顺序（不做随机重排）。</summary>
    [Fact]
    public async Task LoadTranscriptAsync_LegacyEntries_ComeFirstInOriginalOrder() {
        await _service.AppendEntriesAsync("s-order", [
            Entry("user", "有号问题", 1),
        ]).ConfigureAwait(true);
        await _service.AppendEntriesAsync("s-order", [
            Entry("user", "旧问题一", null),
            Entry("assistant", "旧回答一", null),
        ]).ConfigureAwait(true);

        var entries = await _service.LoadTranscriptAsync("s-order").ConfigureAwait(true);

        Assert.Equal(["旧问题一", "旧回答一", "有号问题"], entries.Select(e => e.Content).ToArray());
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        _service.DisposeSafe();
    }
}
