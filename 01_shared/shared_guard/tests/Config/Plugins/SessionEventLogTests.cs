namespace Core.Tests.Plugins;

public sealed class SessionEventLogTests
{
    [Fact]
    public void Append_SeqIncrement()
    {
        var log = new SessionEventLog(() => 1000);
        var e1 = log.Append("user/message");
        var e2 = log.Append("tool/call");
        var e3 = log.Append("tool/result");
        Assert.Equal(1, e1.Seq);
        Assert.Equal(2, e2.Seq);
        Assert.Equal(3, e3.Seq);
    }

    [Fact]
    public void Append_TimeSet()
    {
        var log = new SessionEventLog(() => 5000);
        var evt = log.Append("user/message");
        Assert.Equal(5000, evt.Time);
    }

    [Fact]
    public void Append_TypeAndDataStored()
    {
        var log = new SessionEventLog();
        var evt = log.Append("tool/call", data: "payload");
        Assert.Equal("tool/call", evt.Type);
        Assert.Equal("payload", evt.Data);
    }

    [Fact]
    public void Append_SurfaceOpStored()
    {
        var log = new SessionEventLog();
        var evt = log.Append("user/message", surfaceOp: SurfaceOp.Append());
        Assert.NotNull(evt.SurfaceOp);
        Assert.Equal(SurfaceOpKind.Append, evt.SurfaceOp!.Kind);
    }

    [Fact]
    public void Append_SourceEventSeqsStored()
    {
        var log = new SessionEventLog();
        var evt = log.Append("assistant/message", sourceEventSeqs: new[] { 1, 2 });
        Assert.Equal(new[] { 1, 2 }, evt.SourceEventSeqs);
    }

    [Fact]
    public void Events_ReturnsSnapshot()
    {
        var log = new SessionEventLog();
        log.Append("a");
        log.Append("b");
        var snap1 = log.Events;
        log.Append("c");
        Assert.Equal(2, snap1.Count);
        Assert.Equal(3, log.Events.Count);
    }

    [Fact]
    public void Count_ReturnsEventCount()
    {
        var log = new SessionEventLog();
        Assert.Equal(0, log.Count);
        log.Append("a");
        log.Append("b");
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void Find_BySeq()
    {
        var log = new SessionEventLog();
        log.Append("a");
        var e2 = log.Append("b");
        log.Append("c");
        var found = log.Find(2);
        Assert.NotNull(found);
        Assert.Equal(e2, found);
        Assert.Null(log.Find(999));
    }

    [Fact]
    public void After_GivenSeq()
    {
        var log = new SessionEventLog();
        log.Append("a");
        log.Append("b");
        log.Append("c");
        log.Append("d");
        var after = log.After(2);
        Assert.Equal(2, after.Count);
        Assert.Equal("c", after[0].Type);
        Assert.Equal("d", after[1].Type);
    }

    [Fact]
    public void Until_GivenSeq()
    {
        var log = new SessionEventLog();
        log.Append("a");
        log.Append("b");
        log.Append("c");
        var until = log.Until(2);
        Assert.Equal(2, until.Count);
        Assert.Equal("a", until[0].Type);
        Assert.Equal("b", until[1].Type);
    }

    [Fact]
    public void SurfaceOp_Append()
    {
        var op = SurfaceOp.Append();
        Assert.Equal(SurfaceOpKind.Append, op.Kind);
        Assert.Null(op.StartSeq);
    }

    [Fact]
    public void SurfaceOp_Replace()
    {
        var op = SurfaceOp.Replace(5, 10);
        Assert.Equal(SurfaceOpKind.Replace, op.Kind);
        Assert.Equal(5, op.StartSeq);
        Assert.Equal(10, op.EndSeq);
    }

    [Fact]
    public void ValidateHeader_CurrentVersion_Ok()
    {
        var header = new SessionEventLogHeader { Version = 3, Id = "s1", CreatedAt = 0 };
        SessionEventRebuilder.ValidateHeader(header);
    }

    [Fact]
    public void ValidateHeader_OlderVersion_Ok()
    {
        var header = new SessionEventLogHeader { Version = 2, Id = "s1", CreatedAt = 0 };
        SessionEventRebuilder.ValidateHeader(header);
    }

    [Fact]
    public void ValidateHeader_NewerVersion_Throws()
    {
        var header = new SessionEventLogHeader { Version = 4, Id = "s1", CreatedAt = 0 };
        var ex = Assert.Throws<InvalidOperationException>(() => SessionEventRebuilder.ValidateHeader(header));
        Assert.Contains("[INF-SESSION-FORMAT]", ex.Message);
        Assert.Contains("4", ex.Message);
    }

    [Fact]
    public void Rebuild_KnownTypes_AllAccepted()
    {
        var events = new SessionEvent[]
        {
            new() { Seq = 1, Time = 0, Type = "user/message" },
            new() { Seq = 2, Time = 0, Type = "tool/call" },
        };
        var known = new HashSet<string> { "user/message", "tool/call" };
        var result = SessionEventRebuilder.Rebuild(events, known);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Rebuild_UnknownNonIgnorable_Throws()
    {
        var events = new SessionEvent[]
        {
            new() { Seq = 1, Time = 0, Type = "unknown/type", Ignorable = false },
        };
        var known = new HashSet<string> { "user/message" };
        var ex = Assert.Throws<InvalidOperationException>(() => SessionEventRebuilder.Rebuild(events, known));
        Assert.Contains("[INF-SESSION-UNKNOWN]", ex.Message);
        Assert.Contains("unknown/type", ex.Message);
    }

    [Fact]
    public void Rebuild_UnknownIgnorable_Skipped()
    {
        var events = new SessionEvent[]
        {
            new() { Seq = 1, Time = 0, Type = "user/message" },
            new() { Seq = 2, Time = 0, Type = "unknown/type", Ignorable = true },
        };
        var known = new HashSet<string> { "user/message" };
        var result = SessionEventRebuilder.Rebuild(events, known);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Rebuild_NoKnownTypes_AllAccepted()
    {
        var events = new SessionEvent[]
        {
            new() { Seq = 1, Time = 0, Type = "anything" },
        };
        var result = SessionEventRebuilder.Rebuild(events, knownTypes: null);
        Assert.Single(result);
    }

    [Fact]
    public void SessionFormatVersion_CurrentIs3()
    {
        Assert.Equal(3, SessionFormatVersion.Current);
    }
}
