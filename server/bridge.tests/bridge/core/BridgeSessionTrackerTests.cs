
namespace Bridge.Tests;

/// <summary>
/// BridgeSessionTracker 单元测试
/// 测试会话注册、查询、清理、标题标记等状态管理逻辑
/// </summary>
public sealed class BridgeSessionTrackerTests
{
    private static BridgeSessionTracker CreateSut() => new();

    private static BridgeSessionState CreateState(
        string workId,
        string? ingressToken = null,
        string? worktreePath = null,
        string? compatId = null,
        bool isV2 = false)
        => new()
        {
            Handle = null,
            StartTime = DateTime.UtcNow,
            WorkId = workId,
            IngressToken = ingressToken,
            WorktreePath = worktreePath,
            CompatId = compatId,
            IsV2 = isV2,
        };

    private static void RegisterSession(BridgeSessionTracker sut, string sessionId,
        string workId, string? ingressToken = null, string? worktreePath = null,
        string? compatId = null, bool isV2 = false)
        => sut.Sessions.Register(sessionId, CreateState(workId, ingressToken, worktreePath, compatId, isV2));

    [Fact]
    public void RegisterSession_AddsSessionWithAllOptionalFields()
    {
        var sut = CreateSut();

        RegisterSession(sut, "session-1", "work-1",
            ingressToken: "token-1",
            worktreePath: "C:\\work",
            compatId: "compat-1",
            isV2: true);

        sut.Sessions.Count.Should().Be(1);
        sut.Sessions.Has("session-1").Should().BeTrue();
        sut.Sessions.GetCompatId("session-1").Should().Be("compat-1");
        sut.Sessions.GetIngressToken("session-1").Should().Be("token-1");
        sut.Sessions.TryGetWorktree("session-1", out var worktree).Should().BeTrue();
        worktree.Should().Be("C:\\work");
        sut.Sessions.IsV2("session-1").Should().BeTrue();
        sut.Titles.Has("compat-1").Should().BeFalse();
    }

    [Fact]
    public void RegisterSession_WithoutOptionalFields_StillTracksSession()
    {
        var sut = CreateSut();

        RegisterSession(sut, "session-1", "work-1");

        sut.Sessions.Count.Should().Be(1);
        sut.Sessions.GetIngressToken("session-1").Should().BeNull();
        sut.Sessions.TryGetWorktree("session-1", out _).Should().BeFalse();
        sut.Sessions.IsV2("session-1").Should().BeFalse();
    }

    [Fact]
    public void GetCompatId_UnknownSession_ReturnsSessionId()
    {
        var sut = CreateSut();

        sut.Sessions.GetCompatId("unknown").Should().Be("unknown");
    }

    [Fact]
    public void GetHandle_UnknownSession_ReturnsNull()
    {
        var sut = CreateSut();

        sut.Sessions.GetHandle("unknown").Should().BeNull();
    }

    [Fact]
    public void MarkTitled_ThenHasTitle_ReturnsTrue()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1", compatId: "compat-1");

        sut.Titles.Mark("compat-1");

        sut.Titles.Has("compat-1").Should().BeTrue();
    }

    [Fact]
    public void MarkWorkCompleted_IsWorkCompleted_ReturnsTrue()
    {
        var sut = CreateSut();

        sut.WorkCompletion.Mark("work-1");

        sut.WorkCompletion.IsCompleted("work-1").Should().BeTrue();
        sut.WorkCompletion.IsCompleted("work-2").Should().BeFalse();
    }

    [Fact]
    public void MarkTimedOut_RemoveTimedOut_RoundTrip()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1");

        sut.Sessions.MarkTimedOut("session-1");
        sut.Sessions.RemoveTimedOut("session-1").Should().BeTrue();
        sut.Sessions.RemoveTimedOut("session-1").Should().BeFalse();
    }

    [Fact]
    public void UpdateIngressToken_ChangesStoredToken()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1", ingressToken: "old-token");

        sut.Sessions.UpdateIngressToken("session-1", "new-token");

        sut.Sessions.GetIngressToken("session-1").Should().Be("new-token");
    }

    [Fact]
    public void GetDurationMs_KnownSession_ReturnsElapsed()
    {
        var clock = new FakeClockService();
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1");

        clock.Advance(TimeSpan.FromSeconds(3));

        var duration = sut.Sessions.GetDurationMs("session-1", clock);
        duration.Should().BeGreaterThanOrEqualTo(2999);
    }

    [Fact]
    public void GetDurationMs_UnknownSession_ReturnsZero()
    {
        var sut = CreateSut();
        var clock = new FakeClockService();

        sut.Sessions.GetDurationMs("unknown", clock).Should().Be(0);
    }

    [Fact]
    public void GetAllSessionIds_ReturnsRegisteredSessionIds()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1");
        RegisterSession(sut, "session-2", "work-2");

        var ids = sut.Sessions.GetAllSessionIds();

        ids.Should().Contain("session-1").And.Contain("session-2");
    }

    [Fact]
    public void GetAllWorkIds_ReturnsRegisteredWorkIds()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1");

        sut.Sessions.GetAllWorkIds().Should().Contain("work-1");
    }

    [Fact]
    public void GetLastSession_WithSessions_ReturnsLast()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1");
        sut.Sessions.Register("session-2", CreateState("work-2") with { StartTime = DateTime.UtcNow.AddSeconds(1) });

        var last = sut.Sessions.GetLastSession();

        last.Should().NotBeNull();
        last!.Value.Key.Should().Be("session-2");
    }

    [Fact]
    public void GetLastSession_Empty_ReturnsNull()
    {
        var sut = CreateSut();

        sut.Sessions.GetLastSession().Should().BeNull();
    }

    [Fact]
    public void CleanupSession_RemovesAllRelatedState()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1", ingressToken: "token", compatId: "compat-1");
        sut.Titles.Mark("compat-1");
        var compatRemoved = false;

        sut.CleanupSession("session-1", compatId => compatRemoved = true);

        sut.Sessions.Count.Should().Be(0);
        sut.Sessions.Has("session-1").Should().BeFalse();
        sut.Sessions.GetIngressToken("session-1").Should().BeNull();
        sut.Titles.Has("compat-1").Should().BeFalse();
        compatRemoved.Should().BeTrue();
    }

    [Fact]
    public void RemoveWorktree_RemovesAndReturnsPath()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1", worktreePath: "C:\\work");

        sut.Sessions.RemoveWorktree("session-1", out var path).Should().BeTrue();
        path.Should().Be("C:\\work");
        sut.Sessions.TryGetWorktree("session-1", out _).Should().BeFalse();
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1", compatId: "compat-1");
        sut.Titles.Mark("compat-1");
        sut.WorkCompletion.Mark("work-1");
        sut.Sessions.MarkTimedOut("session-1");

        sut.ClearAll();

        sut.Sessions.Count.Should().Be(0);
        sut.Sessions.Has("session-1").Should().BeFalse();
        sut.WorkCompletion.IsCompleted("work-1").Should().BeFalse();
        sut.Titles.Has("compat-1").Should().BeFalse();
    }

    [Fact]
    public void Sessions_RegistryExposedForMiddleware()
    {
        var sut = CreateSut();
        RegisterSession(sut, "session-1", "work-1");

        sut.Sessions.Has("session-1").Should().BeTrue();
        sut.Sessions.Count.Should().Be(1);
        sut.WorkCompletion.Should().NotBeNull();
        sut.Titles.Should().NotBeNull();
    }

    [Fact]
    public async Task Concurrent_Register_And_Enumerate_DoesNotThrow()
    {
        var sut = CreateSut();
        for (var i = 0; i < 100; i++)
            RegisterSession(sut, $"session-{i}", "work");

        var exceptions = new ConcurrentQueue<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));

        var enumeratorTask = Task.Run(() =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    foreach (var _ in sut.Sessions.GetAllHandles()) { }
                    foreach (var _ in sut.Sessions.GetAllSessionIds()) { }
                }
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        });

        var modifierTask = Task.Run(() =>
        {
            try
            {
                var i = 100;
                while (!cts.IsCancellationRequested)
                    RegisterSession(sut, $"session-{i++}", "work");
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        });

        var cleanupTask = Task.Run(() =>
        {
            try
            {
                var i = 0;
                while (!cts.IsCancellationRequested)
                    sut.CleanupSession($"session-{i++ % 200}");
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        });

        await Task.WhenAll(enumeratorTask, modifierTask, cleanupTask);
        exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_MarkCompleted_And_Check_DoesNotThrow()
    {
        var sut = CreateSut();
        var exceptions = new ConcurrentQueue<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var writerTask = Task.Run(() =>
        {
            try
            {
                var i = 0;
                while (!cts.IsCancellationRequested)
                    sut.WorkCompletion.Mark($"work-{i++}");
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        });

        var readerTask = Task.Run(() =>
        {
            try
            {
                var i = 0;
                while (!cts.IsCancellationRequested)
                    sut.WorkCompletion.IsCompleted($"work-{i++ % 1000}");
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        });

        await Task.WhenAll(writerTask, readerTask);
        exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_ClearAll_And_Register_DoesNotThrow()
    {
        var sut = CreateSut();
        var exceptions = new ConcurrentQueue<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var clearTask = Task.Run(() =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                    sut.ClearAll();
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        });

        var registerTask = Task.Run(() =>
        {
            try
            {
                var i = 0;
                while (!cts.IsCancellationRequested)
                    RegisterSession(sut, $"session-{i++}", "work");
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        });

        await Task.WhenAll(clearTask, registerTask);
        exceptions.Should().BeEmpty();
    }
}
