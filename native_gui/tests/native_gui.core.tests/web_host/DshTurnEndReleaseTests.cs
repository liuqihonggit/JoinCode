namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// turn/end 必须与轮次占用释放原子化（CI 实证：<see cref="DshTurnRunnerTests.Start_PlanModeTwice_PublishesPlanModeOnce"/>
/// 偶发 session/busy）。旧顺序是先落 turn/end、finally 里再 EndTurn，客户端收到 turn/end 立即发下一条会撞上 busy。
/// </summary>
public sealed class DshTurnEndReleaseTests {
    private static async Task<(DshHostServices Services, string SessionId, State.TranscriptService Transcripts)> CreateAsync() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        return (services, sessionId, transcripts);
    }

    [Fact]
    public async Task PublishTurnEnd_ReleasesSessionWithinSameTransaction() {
        var (services, sessionId, transcripts) = await CreateAsync();
        await using var _ = transcripts;

        if (services.Streams.TryBeginTurn(sessionId) is not { } turn) {
            throw new Xunit.Sdk.XunitException("首轮应能占用会话");
        }

        await services.Streams.PublishTurnEndAsync(sessionId, new DshTurnEndData {
            Turn = turn.Turn,
            Reason = new DshTurnEndReason { Kind = "completed" },
        });

        services.Streams.TryBeginTurn(sessionId).HasValue.Should().BeTrue(
            "客户端收到 turn/end 后立即发送下一条，不得再返回 session/busy");
    }

    [Fact]
    public void EndTurnIfCurrent_DoesNotReleaseNewerTurn() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);

        if (services.Streams.TryBeginTurn("s1") is not { } first) {
            throw new Xunit.Sdk.XunitException("首轮应能占用会话");
        }
        services.Streams.EndTurnIfCurrent("s1", first.Turn);

        if (services.Streams.TryBeginTurn("s1") is not { } second) {
            throw new Xunit.Sdk.XunitException("释放后应能开新轮");
        }

        services.Streams.EndTurnIfCurrent("s1", first.Turn);
        services.Streams.RunningTurn("s1").Should().Be(second.Turn,
            "迟到的旧轮 finally 不得释放新轮占用");

        services.Streams.EndTurnIfCurrent("s1", second.Turn);
        services.Streams.RunningTurn("s1").Should().Be(0);
    }
}
