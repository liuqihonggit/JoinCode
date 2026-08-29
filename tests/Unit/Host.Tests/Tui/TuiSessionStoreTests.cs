namespace Host.Tests.Tui;

/// <summary>
/// TUI 会话持久化存储测试（T6）— 每轮对话增量写 transcript，三端可 /resume。
/// 回归背景：TUI 此前零持久化（JoinCodeTui 无任何 TranscriptService 引用），
/// 进程退出对话即丢、无法被 CLI/GUI resume。T6 对齐 CLI 增量 AppendEntries 语义，
/// sessionId 复用 JoinCode.Cli.SessionIdGenerator（{yyyyMMdd-HHmm}-{项目名}-{分支名} 可读格式）。
/// </summary>
public sealed class TuiSessionStoreTests
{
    private static (TuiSessionStore Store, Mock<ITranscriptService> Transcript) Create(
        string? workingDir = null, DateTime? createdAt = null)
    {
        var transcript = new Mock<ITranscriptService>();
        var store = new TuiSessionStore(transcript.Object, workingDir, createdAt);
        return (store, transcript);
    }

    [Fact]
    public void SessionId_UsesCliReadableFormat()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tuitest-proj");
        var (store, _) = Create(dir, new DateTime(2026, 8, 22, 7, 12, 0, DateTimeKind.Utc));

        // 非 git 目录 → 分支回退 no-branch；T10 五段式末段为 ObjectId 全局递增数
        store.SessionId.Should().MatchRegex(@"^20260822-0712-tuitest-proj-no-branch-parent-\d+$");
    }

    [Fact]
    public async Task SaveMetaAsync_WritesSessionInfo_WithConfigSnapshot()
    {
        var (store, transcript) = Create();
        var config = new WorkflowConfig();
        config.Provider.Vendor = "anthropic";
        config.Provider.ModelId = "claude-sonnet-4";

        await store.SaveMetaAsync(config);

        transcript.Verify(t => t.SaveSessionInfoAsync(
            store.SessionId,
            It.Is<SessionInfo>(info => info.Id == store.SessionId
                && info.ModelId == "claude-sonnet-4"
                && info.Vendor == "anthropic"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // === T7：会话切换 ===

    [Fact]
    public async Task ListSessionsAsync_DelegatesToTranscriptService()
    {
        var (store, transcript) = Create();
        var summaries = new List<TranscriptSummary> { new() { SessionId = "s1", MessageCount = 3 } };
        transcript.Setup(t => t.ListTranscriptsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaries);

        var result = await store.ListSessionsAsync();

        result.Should().HaveCount(1);
        result[0].SessionId.Should().Be("s1");
        transcript.Verify(t => t.ListTranscriptsAsync(20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TryResolveTarget_ByIndex_ReturnsSummaryAtPosition()
    {
        var summaries = new[]
        {
            new TranscriptSummary { SessionId = "aaa" },
            new TranscriptSummary { SessionId = "bbb" },
            new TranscriptSummary { SessionId = "ccc" },
        };

        var ok = TuiSessionStore.TryResolveTarget("2", summaries, out var target);

        ok.Should().BeTrue();
        target.Should().Be("bbb", "序号 1-based 对齐用户直觉");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("")]
    public void TryResolveTarget_IndexOutOfRange_ReturnsFalse(string arg)
    {
        var summaries = new[] { new TranscriptSummary { SessionId = "aaa" } };

        var ok = TuiSessionStore.TryResolveTarget(arg, summaries, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolveTarget_RawId_PassesThrough()
    {
        var summaries = new[] { new TranscriptSummary { SessionId = "aaa" } };

        var ok = TuiSessionStore.TryResolveTarget("20260822-1200-myproj-main", summaries, out var target);

        ok.Should().BeTrue();
        target.Should().Be("20260822-1200-myproj-main");
    }

    [Fact]
    public async Task SwitchToAsync_SwitchesEngineBucketAndUpdatesCurrentId()
    {
        var (store, _) = Create();
        var ctxMgr = new Mock<IChatContextManager>();
        var chatService = new Mock<IChatService>();

        await store.SwitchToAsync(ctxMgr.Object, chatService.Object, "target-session");

        store.SessionId.Should().Be("target-session");
        ctxMgr.Verify(c => c.SwitchSession("target-session"), Times.Once);
        chatService.VerifyNoOtherCalls();
    }
}
