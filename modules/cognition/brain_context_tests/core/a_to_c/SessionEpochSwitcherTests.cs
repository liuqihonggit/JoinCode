namespace Core.Tests.Context;

/// <summary>
/// ADR 0167 红测试 — `/clear` 从「清当前桶」改为「会话翻页」：
/// 新生 sessionId、切换指针、旧桶按需回收、新会话元数据落盘。
/// 现状（红）：SessionEpochSwitcher 类型尚不存在。
/// </summary>
public sealed class SessionEpochSwitcherTests {
    private static ChatContextManager CreateSut(string sessionId) =>
        new(new Mock<IStateService>().Object, NullLogger<ChatContextManager>.Instance,
            new ChatContextOptions { SessionId = sessionId });

    [Fact]
    public async Task BeginNew_SwitchesToFreshBucket_LeavesOldReadable() {
        await using var sut = CreateSut("s-old");
        await sut.AddUserMessageAsync("清空前的问题").ConfigureAwait(true);

        var newId = await SessionEpochSwitcher.BeginNewAsync(
            sut, transcriptService: null,
            new SessionEpochOptions { ReleaseOldBucket = false }).ConfigureAwait(true);

        newId.Should().NotBe("s-old");
        sut.SessionId.Should().Be(newId);
        sut.CurrentMessageCount.Should().Be(0, "新代必须是空桶");

        sut.SwitchSession("s-old");
        sut.CurrentMessageCount.Should().Be(1, "旧代内容在翻页后仍必须完整可读（供 /resume 旧id）");
    }

    [Fact]
    public async Task BeginNew_EnsuresTranscriptSessionForNewId() {
        await using var sut = CreateSut("s-old");
        var transcript = new Mock<ITranscriptService>();

        var newId = await SessionEpochSwitcher.BeginNewAsync(
            sut, transcript.Object,
            new SessionEpochOptions { ProjectPath = "/repo", ReleaseOldBucket = false }).ConfigureAwait(true);

        transcript.Verify(t => t.EnsureSessionAsync(
            newId,
            It.Is<SessionInfo>(info => info.ProjectPath == "/repo"),
            It.IsAny<CancellationToken>()), Times.Once,
            "新代必须有 meta.json，否则 /resume 列表与 TUI 历史预热都扫不到它");
    }

    [Fact]
    public async Task BeginNew_WhenReleasingOldBucket_OldContentIsGone() {
        await using var sut = CreateSut("s-old");
        await sut.AddUserMessageAsync("旧代消息").ConfigureAwait(true);

        await SessionEpochSwitcher.BeginNewAsync(
            sut, transcriptService: null,
            new SessionEpochOptions { ReleaseOldBucket = true }).ConfigureAwait(true);

        sut.SwitchSession("s-old");
        sut.CurrentMessageCount.Should().Be(0, "无在途查询时旧桶应被回收，避免 _logs 只增不减");
    }

    [Fact]
    public async Task BeginNew_WhenTranscriptThrows_StillSwitchesSession() {
        await using var sut = CreateSut("s-old");
        var transcript = new Mock<ITranscriptService>();
        transcript.Setup(t => t.EnsureSessionAsync(It.IsAny<string>(), It.IsAny<SessionInfo>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        var newId = await SessionEpochSwitcher.BeginNewAsync(
            sut, transcript.Object,
            new SessionEpochOptions { ReleaseOldBucket = false }).ConfigureAwait(true);

        sut.SessionId.Should().Be(newId, "元数据落盘失败不得阻断 /clear（降级为无 meta 的新会话）");
    }
}
