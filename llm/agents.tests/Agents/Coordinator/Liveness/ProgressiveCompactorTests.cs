namespace Sync.Tests.Agents.Coordinator.Liveness;

/// <summary>
/// ProgressiveCompactor 单元测试 — 验证渐进式压缩 Light→Aggressive→ExitWithSummary（ADR 0106 L4）
/// </summary>
public sealed class ProgressiveCompactorTests
{
    private static Mock<IChatContextManager> CreateContextMock()
    {
        var mock = new Mock<IChatContextManager>();
        mock.Setup(x => x.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        return mock;
    }

    [Fact]
    public async Task CompactProgressiveAsync_LightFoldSucceeds_ReturnsLightLevel()
    {
        var ctxMock = CreateContextMock();
        ctxMock
            .Setup(x => x.FoldIfNeededAsync(It.IsAny<ContextFoldDecision>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextFoldResult { Folded = true, Decision = ContextFoldDecision.FoldNormal });
        var compactor = new ProgressiveCompactor(ctxMock.Object);

        var result = await compactor.CompactProgressiveAsync("agent-1");

        result.Level.Should().Be(CompactionLevel.Light);
        result.Success.Should().BeTrue();
        result.FoldResult.Should().NotBeNull();
        ctxMock.Verify(x => x.FoldIfNeededAsync(ContextFoldDecision.FoldNormal, "agent-1", It.IsAny<CancellationToken>()), Times.Once);
        ctxMock.Verify(x => x.FoldIfNeededAsync(ContextFoldDecision.FoldAggressive, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompactProgressiveAsync_LightFails_AggressiveSucceeds_ReturnsAggressiveLevel()
    {
        var ctxMock = CreateContextMock();
        ctxMock.SetupSequence(x => x.FoldIfNeededAsync(It.IsAny<ContextFoldDecision>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextFoldResult { Folded = false, Decision = ContextFoldDecision.FoldNormal })
            .ReturnsAsync(new ContextFoldResult { Folded = true, Decision = ContextFoldDecision.FoldAggressive });
        var compactor = new ProgressiveCompactor(ctxMock.Object);

        var result = await compactor.CompactProgressiveAsync("agent-1");

        result.Level.Should().Be(CompactionLevel.Aggressive);
        result.Success.Should().BeTrue();
        result.FoldResult.Should().NotBeNull();
        ctxMock.Verify(x => x.FoldIfNeededAsync(ContextFoldDecision.FoldNormal, "agent-1", It.IsAny<CancellationToken>()), Times.Once);
        ctxMock.Verify(x => x.FoldIfNeededAsync(ContextFoldDecision.FoldAggressive, "agent-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompactProgressiveAsync_BothFoldFail_ReturnsExitWithSummary()
    {
        var ctxMock = CreateContextMock();
        ctxMock.SetupSequence(x => x.FoldIfNeededAsync(It.IsAny<ContextFoldDecision>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextFoldResult { Folded = false, Decision = ContextFoldDecision.FoldNormal })
            .ReturnsAsync(new ContextFoldResult { Folded = false, Decision = ContextFoldDecision.FoldAggressive });
        var compactor = new ProgressiveCompactor(ctxMock.Object);

        var result = await compactor.CompactProgressiveAsync("agent-1");

        result.Level.Should().Be(CompactionLevel.ExitWithSummary);
        result.Success.Should().BeTrue();
        result.Summary.Should().NotBeNull();
        result.Summary.Should().Contain("agent-1");
    }

    [Fact]
    public async Task CompactProgressiveAsync_ExitWithSummary_ContainsRecentMessages()
    {
        var messages = new MessageList();
        messages.AddUserMessage("do task A");
        messages.AddAssistantMessage("working on A");
        messages.AddUserMessage("continue");
        var ctxMock = new Mock<IChatContextManager>();
        ctxMock.SetupSequence(x => x.FoldIfNeededAsync(It.IsAny<ContextFoldDecision>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextFoldResult { Folded = false })
            .ReturnsAsync(new ContextFoldResult { Folded = false });
        ctxMock.Setup(x => x.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);
        var compactor = new ProgressiveCompactor(ctxMock.Object);

        var result = await compactor.CompactProgressiveAsync("agent-42");

        result.Summary.Should().Contain("do task A");
        result.Summary.Should().Contain("working on A");
        result.Summary.Should().Contain("continue");
    }

    [Fact]
    public async Task CompactProgressiveAsync_NullAgentId_ThrowsArgumentNullException()
    {
        var compactor = new ProgressiveCompactor(CreateContextMock().Object);

        var act = () => compactor.CompactProgressiveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
