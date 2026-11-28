namespace State.Tests;

/// <summary>
/// TASK142-D — 崩溃收编的**真实磁盘**验收（TASK135 C2 从 mock 文件系统升级）。
/// <para>模拟方式：真实 `PhysicalFileSystem` 写旁路 → 丢弃 writer 实例（等价于进程被 kill -9，
/// 不再调用任何清理/收尾）→ 新实例（等价于重启）加载 transcript。判据是磁盘上已写行不丢且只收编一次。
/// 注意：**这不是进程级 kill -9**，不冒充；进程级场景另见 TASK142 §5.D 的证据边界。</para>
/// </summary>
public sealed class TranscriptCrashRecoveryRealFsTests : IDisposable {
    private readonly IFileSystem _fs = new PhysicalFileSystem();
    private readonly string _root;
    private int _disposed;

    /// <summary>构造隔离的真实临时目录。</summary>
    public TranscriptCrashRecoveryRealFsTests() =>
        _root = Path.Combine(Path.GetTempPath(), "jcc-t142-crash-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 旁路最后一条快照在"崩溃"后仍在磁盘上，重启加载时收编进主 transcript 并删除旁路；
    /// 二次加载不再重复追加（收编是一次性动作）。
    /// </summary>
    [Fact]
    public async Task SidecarWrittenBeforeCrash_SurvivesRestart_AndIsAdoptedExactlyOnce() {
        const string sessionId = "s-t142-crash";
        const string queryId = "q-t142-crash";
        const string partial = "崩溃前已生成并落盘的正文";
        var sidecar = Path.Combine(_root, sessionId, $"streaming.{queryId}.jsonl");

        await using var beforeCrash = new TranscriptService(_fs, _root);
        await beforeCrash.AppendEntryAsync(sessionId, new TranscriptEntry {
            Role = "user", Content = "问题", SessionId = sessionId, Timestamp = DateTime.UtcNow,
        }).ConfigureAwait(true);
        await beforeCrash.AppendStreamingSnapshotAsync(sessionId, queryId, 1, partial).ConfigureAwait(true);

        // 等价 kill -9：不 Dispose、不清旁路、直接丢弃实例后从磁盘校验
        _fs.FileExists(sidecar).Should().BeTrue("每次快照都是打开-写入-关闭，已写行不应依赖进程存活");
        (await _fs.ReadAllTextAsync(sidecar).ConfigureAwait(true)).Should().Contain(partial);

        await using var afterRestart = new TranscriptService(_fs, _root);
        var adopted = await afterRestart.LoadTranscriptAsync(sessionId).ConfigureAwait(true);
        adopted.Should().ContainSingle(e => e.Content == partial);
        adopted.Should().ContainSingle(e => e.Type == TranscriptService.StreamingEntryType);
        _fs.FileExists(sidecar).Should().BeFalse("收编即消费，旁路必须删除");

        var reloaded = await afterRestart.LoadTranscriptAsync(sessionId).ConfigureAwait(true);
        reloaded.Should().ContainSingle(e => e.Content == partial, "重复加载不得重复收编");
    }

    /// <summary>多个查询的旁路各自独立：一个查询的崩溃不能丢掉另一个查询的残稿。</summary>
    [Fact]
    public async Task MultipleQuerySidecars_AllSurviveCrash_AndAreAdoptedSeparately() {
        const string sessionId = "s-t142-multi";
        await using var beforeCrash = new TranscriptService(_fs, _root);
        await beforeCrash.AppendStreamingSnapshotAsync(sessionId, "q-a", 1, "A 的残稿").ConfigureAwait(true);
        await beforeCrash.AppendStreamingSnapshotAsync(sessionId, "q-b", 2, "B 的残稿").ConfigureAwait(true);

        await using var afterRestart = new TranscriptService(_fs, _root);
        var adopted = await afterRestart.LoadTranscriptAsync(sessionId).ConfigureAwait(true);

        adopted.Should().ContainSingle(e => e.Content == "A 的残稿");
        adopted.Should().ContainSingle(e => e.Content == "B 的残稿");
        _fs.FileExists(Path.Combine(_root, sessionId, "streaming.q-a.jsonl")).Should().BeFalse();
        _fs.FileExists(Path.Combine(_root, sessionId, "streaming.q-b.jsonl")).Should().BeFalse();
    }

    /// <summary>按 Xunit 约定清理真实临时目录。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_fs.DirectoryExists(_root))
            _fs.DeleteDirectory(_root, recursive: true);
    }
}
