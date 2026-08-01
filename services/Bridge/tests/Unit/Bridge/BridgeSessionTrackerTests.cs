
namespace Bridge.Tests;

/// <summary>
/// BridgeSessionTracker 单元测试
/// 测试会话注册、查询、清理、标题标记等状态管理逻辑
/// </summary>
public sealed class BridgeSessionTrackerTests
{
    private static BridgeSessionTracker CreateSut() => new();

    [Fact]
    public void RegisterSession_AddsSessionWithAllOptionalFields()
    {
        var sut = CreateSut();
        var handle = (BridgeSubprocessHandle?)null;

        sut.RegisterSession(
            "session-1",
            handle!,
            "work-1",
            ingressToken: "token-1",
            worktreePath: "C:\\work",
            compatId: "compat-1",
            isV2: true);

        sut.ActiveSessionCount.Should().Be(1);
        sut.HasSession("session-1").Should().BeTrue();
        sut.GetCompatId("session-1").Should().Be("compat-1");
        sut.GetIngressToken("session-1").Should().Be("token-1");
        sut.TryGetWorktree("session-1", out var worktree).Should().BeTrue();
        worktree.Should().Be("C:\\work");
        sut.IsV2Session("session-1").Should().BeTrue();
        sut.HasTitle("compat-1").Should().BeFalse();
    }

    [Fact]
    public void RegisterSession_WithoutOptionalFields_StillTracksSession()
    {
        var sut = CreateSut();

        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1");

        sut.ActiveSessionCount.Should().Be(1);
        sut.GetIngressToken("session-1").Should().BeNull();
        sut.TryGetWorktree("session-1", out _).Should().BeFalse();
        sut.IsV2Session("session-1").Should().BeFalse();
    }

    [Fact]
    public void GetCompatId_UnknownSession_ReturnsSessionId()
    {
        var sut = CreateSut();

        sut.GetCompatId("unknown").Should().Be("unknown");
    }

    [Fact]
    public void GetSession_UnknownSession_ReturnsNull()
    {
        var sut = CreateSut();

        sut.GetSession("unknown").Should().BeNull();
    }

    [Fact]
    public void MarkTitled_ThenHasTitle_ReturnsTrue()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1", compatId: "compat-1");

        sut.MarkTitled("compat-1");

        sut.HasTitle("compat-1").Should().BeTrue();
    }

    [Fact]
    public void MarkWorkCompleted_IsWorkCompleted_ReturnsTrue()
    {
        var sut = CreateSut();

        sut.MarkWorkCompleted("work-1");

        sut.IsWorkCompleted("work-1").Should().BeTrue();
        sut.IsWorkCompleted("work-2").Should().BeFalse();
    }

    [Fact]
    public void MarkTimedOut_RemoveTimedOut_RoundTrip()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1");

        sut.MarkTimedOut("session-1");
        sut.RemoveTimedOut("session-1").Should().BeTrue();
        sut.RemoveTimedOut("session-1").Should().BeFalse();
    }

    [Fact]
    public void UpdateIngressToken_ChangesStoredToken()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1", ingressToken: "old-token");

        sut.UpdateIngressToken("session-1", "new-token");

        sut.GetIngressToken("session-1").Should().Be("new-token");
    }

    [Fact]
    public void GetSessionDurationMs_KnownSession_ReturnsElapsed()
    {
        var clock = new FakeClockService();
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1");

        clock.Advance(TimeSpan.FromSeconds(3));

        var duration = sut.GetSessionDurationMs("session-1", clock);
        duration.Should().BeGreaterThanOrEqualTo(2999);
    }

    [Fact]
    public void GetSessionDurationMs_UnknownSession_ReturnsZero()
    {
        var sut = CreateSut();
        var clock = new FakeClockService();

        sut.GetSessionDurationMs("unknown", clock).Should().Be(0);
    }

    [Fact]
    public void GetAllSessionIds_ReturnsRegisteredSessionIds()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1");
        sut.RegisterSession("session-2", (BridgeSubprocessHandle?)null!, "work-2");

        var ids = sut.GetAllSessionIds();

        ids.Should().Contain("session-1").And.Contain("session-2");
    }

    [Fact]
    public void GetAllWorkIds_ReturnsRegisteredWorkIds()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1");

        sut.GetAllWorkIds().Should().Contain("work-1");
    }

    [Fact]
    public void GetLastSession_WithSessions_ReturnsLast()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1");
        sut.RegisterSession("session-2", (BridgeSubprocessHandle?)null!, "work-2");

        var last = sut.GetLastSession();

        last.Should().NotBeNull();
        last!.Value.Key.Should().Be("session-2");
    }

    [Fact]
    public void GetLastSession_Empty_ReturnsNull()
    {
        var sut = CreateSut();

        sut.GetLastSession().Should().BeNull();
    }

    [Fact]
    public void CleanupSession_RemovesAllRelatedState()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1", ingressToken: "token", compatId: "compat-1");
        sut.MarkTitled("compat-1");
        var compatRemoved = false;

        sut.CleanupSession("session-1", compatId => compatRemoved = true);

        sut.ActiveSessionCount.Should().Be(0);
        sut.HasSession("session-1").Should().BeFalse();
        sut.GetIngressToken("session-1").Should().BeNull();
        sut.HasTitle("compat-1").Should().BeFalse();
        compatRemoved.Should().BeTrue();
    }

    [Fact]
    public void RemoveWorktree_RemovesAndReturnsPath()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1", worktreePath: "C:\\work");

        sut.RemoveWorktree("session-1", out var path).Should().BeTrue();
        path.Should().Be("C:\\work");
        sut.TryGetWorktree("session-1", out _).Should().BeFalse();
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1", compatId: "compat-1");
        sut.MarkTitled("compat-1");
        sut.MarkWorkCompleted("work-1");
        sut.MarkTimedOut("session-1");

        sut.ClearAll();

        sut.ActiveSessionCount.Should().Be(0);
        sut.HasSession("session-1").Should().BeFalse();
        sut.IsWorkCompleted("work-1").Should().BeFalse();
        sut.HasTitle("compat-1").Should().BeFalse();
    }

    [Fact]
    public void InternalCollections_ExposedForMiddleware()
    {
        var sut = CreateSut();
        sut.RegisterSession("session-1", (BridgeSubprocessHandle?)null!, "work-1");

        sut.ActiveSessions.Should().ContainKey("session-1");
        sut.SessionStartTimes.Should().ContainKey("session-1");
        sut.SessionWorkIds.Should().ContainKey("session-1");
        sut.CompletedWorkIds.Should().NotBeNull();
        sut.V2Sessions.Should().NotBeNull();
    }
}
