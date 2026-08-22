using JoinCode.Abstractions.Configuration;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Tui.Session;

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

        // 非 git 目录 → 分支回退 no-branch；格式对齐 CLI SessionIdGenerator
        store.SessionId.Should().MatchRegex(@"^20260822-0712-tuitest-proj-no-branch$");
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
}
