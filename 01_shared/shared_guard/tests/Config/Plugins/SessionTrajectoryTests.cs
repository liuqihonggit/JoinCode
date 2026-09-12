namespace Core.Tests.Plugins;

public sealed class SessionTrajectoryTests
{
    private static SessionEventLog BuildLog(params string[] types)
    {
        var log = new SessionEventLog(() => 1000);
        foreach (var t in types) log.Append(t);
        return log;
    }

    [Fact]
    public void FullTrajectory_ReturnsAllEventsInOrder()
    {
        var log = BuildLog("user/message", "tool/call", "tool/result");
        var traj = new SessionTrajectory(log);
        Assert.Equal(3, traj.FullTrajectory.Count);
        Assert.Equal("user/message", traj.FullTrajectory[0].Type);
        Assert.Equal("tool/result", traj.FullTrajectory[2].Type);
    }

    [Fact]
    public void GroupByType_GroupsCorrectly()
    {
        var log = BuildLog("user/message", "tool/call", "tool/call", "tool/result");
        var traj = new SessionTrajectory(log);
        var groups = traj.GroupByType();
        Assert.Equal(3, groups.Count);
        Assert.Single(groups["user/message"]);
        Assert.Equal(2, groups["tool/call"].Count);
        Assert.Single(groups["tool/result"]);
    }

    [Fact]
    public void OfType_FiltersByType()
    {
        var log = BuildLog("a", "b", "a", "c", "a");
        var traj = new SessionTrajectory(log);
        var aEvents = traj.OfType("a");
        Assert.Equal(3, aEvents.Count);
        Assert.All(aEvents, e => Assert.Equal("a", e.Type));
    }

    [Fact]
    public void OfType_NonExistent_ReturnsEmpty()
    {
        var log = BuildLog("a", "b");
        var traj = new SessionTrajectory(log);
        Assert.Empty(traj.OfType("z"));
    }

    [Fact]
    public void TotalCount_ReturnsLogCount()
    {
        var log = BuildLog("a", "b", "c");
        var traj = new SessionTrajectory(log);
        Assert.Equal(3, traj.TotalCount);
    }

    [Fact]
    public void ReplayUntil_ReturnsEventsUpToSeq()
    {
        var log = BuildLog("a", "b", "c", "d");
        var replayed = SessionReplay.ReplayUntil(log, 2);
        Assert.Equal(2, replayed.Count);
        Assert.Equal("a", replayed[0].Type);
        Assert.Equal("b", replayed[1].Type);
    }

    [Fact]
    public void ReplayAfter_ReturnsEventsAfterSeq()
    {
        var log = BuildLog("a", "b", "c", "d");
        var replayed = SessionReplay.ReplayAfter(log, 2);
        Assert.Equal(2, replayed.Count);
        Assert.Equal("c", replayed[0].Type);
        Assert.Equal("d", replayed[1].Type);
    }

    [Fact]
    public void Fork_CopiesPrefixToNewLog()
    {
        var log = BuildLog("a", "b", "c", "d");
        var forked = SessionReplay.Fork(log, atSeq: 2, clock: () => 2000);
        Assert.Equal(2, forked.Count);
        Assert.Equal("a", forked.Events[0].Type);
        Assert.Equal("b", forked.Events[1].Type);
    }

    [Fact]
    public void Fork_NewLogHasIndependentSeq()
    {
        var log = BuildLog("a", "b", "c");
        var forked = SessionReplay.Fork(log, atSeq: 3);
        Assert.Equal(1, forked.Events[0].Seq);
        Assert.Equal(2, forked.Events[1].Seq);
        Assert.Equal(3, forked.Events[2].Seq);
    }

    [Fact]
    public void Fork_AtSeq0_ReturnsEmpty()
    {
        var log = BuildLog("a", "b");
        var forked = SessionReplay.Fork(log, atSeq: 0);
        Assert.Equal(0, forked.Count);
    }

    [Fact]
    public void Fork_PreservesEventData()
    {
        var log = new SessionEventLog(() => 1000);
        log.Append("tool/call", data: "payload", surfaceOp: SurfaceOp.Append());
        log.Append("tool/result");
        var forked = SessionReplay.Fork(log, atSeq: 2);
        Assert.Equal("payload", forked.Events[0].Data);
        Assert.NotNull(forked.Events[0].SurfaceOp);
    }

    [Fact]
    public void ForkAndContinue_CanAppendNewEvents()
    {
        var log = BuildLog("a", "b", "c");
        var forked = SessionReplay.ForkAndContinue(log, atSeq: 2);
        forked.Append("d");
        Assert.Equal(3, forked.Count);
        Assert.Equal("d", forked.Events[2].Type);
    }

    [Fact]
    public void Fork_IndependentFromSource()
    {
        var log = BuildLog("a", "b");
        var forked = SessionReplay.Fork(log, atSeq: 2);
        log.Append("c");
        Assert.Equal(2, forked.Count);
        Assert.Equal(3, log.Count);
    }

    [Fact]
    public void Trajectory_NullLog_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionTrajectory(null!));
    }
}
