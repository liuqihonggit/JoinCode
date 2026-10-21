namespace NativeTui.Tests.Repl.Transcript;

/// <summary>
/// TranscriptSnapshot 语义测试 — 对齐基准 REPL.tsx 4237-4248（进入冻结 / 退出恢复 live）：
/// record 透传（行数 + 流式长度）、Controller 进入时一次性拍摄 + 防御拷贝隔离 live
/// 原地变更、提供器每次进入调用一次、退出快照丢弃、再次进入重新拍摄。
/// </summary>
public sealed class TranscriptSnapshotTests
{
    /// <summary>record — 行数与流式长度透传。</summary>
    [Fact]
    public void Snapshot_LineCount_AndStreamingLength()
    {
        var snapshot = new TranscriptSnapshot(["a", "b", "c"], 42);
        Assert.Equal(3, snapshot.LineCount);
        Assert.Equal(42, snapshot.StreamingLength);
    }

    /// <summary>record — 值相等（同名同长即等）。</summary>
    [Fact]
    public void Snapshot_ValueEquality()
    {
        Assert.Equal(new TranscriptSnapshot(["a"], 0), new TranscriptSnapshot(["a"], 0));
        Assert.NotEqual(new TranscriptSnapshot(["a"], 1), new TranscriptSnapshot(["a"], 0));
    }

    /// <summary>进入 — 快照冻结：拍摄后 live 源原地变更不影响快照行（防御拷贝隔离）。</summary>
    [Fact]
    public void Enter_FrozenAgainstLiveMutation()
    {
        var lines = new List<string> { "a", "b" };
        var controller = NewController(lines);
        controller.Toggle();
        var frozen = controller.Snapshot;
        Assert.NotNull(frozen);
        lines.Add("c");
        Assert.Equal(2, frozen!.LineCount);
        Assert.Equal(2, controller.ContentLineCount);
    }

    /// <summary>进入 — 拍摄时快照与 live 内容一致（流式长度透传）。</summary>
    [Fact]
    public void Enter_CapturesContentAndStreamingLength()
    {
        var controller = new TranscriptController(
            () => new TranscriptSnapshot(["x", "y", "z"], 17), _ => { });
        controller.Toggle();
        Assert.NotNull(controller.Snapshot);
        Assert.Equal(["x", "y", "z"], controller.Snapshot!.Lines);
        Assert.Equal(17, controller.Snapshot.StreamingLength);
    }

    /// <summary>退出 — 快照丢弃恢复 live（null）。</summary>
    [Fact]
    public void Exit_SnapshotDiscarded()
    {
        var controller = NewController(["a"]);
        controller.Toggle();
        controller.Toggle();
        Assert.False(controller.IsActive);
        Assert.Null(controller.Snapshot);
        Assert.Equal(0, controller.ContentLineCount);
    }

    /// <summary>再次进入 — 重新拍摄新数据（live 变化后进入看到新内容）。</summary>
    [Fact]
    public void Reenter_TakesFreshSnapshot()
    {
        var lines = new List<string> { "a" };
        var controller = NewController(lines);
        controller.Toggle();
        controller.Toggle();
        lines.Add("b");
        controller.Toggle();
        Assert.NotNull(controller.Snapshot);
        Assert.Equal(2, controller.Snapshot!.LineCount);
    }

    /// <summary>提供器 — 每次进入调用一次（退出不调用；live 拍摄时机 = 进入瞬间）。</summary>
    [Fact]
    public void Provider_CalledOncePerEnter()
    {
        var calls = 0;
        var lines = new List<string> { "a" };
        var controller = new TranscriptController(
            () => { calls++; return new TranscriptSnapshot(lines, 0); }, _ => { });
        controller.Toggle();
        controller.Toggle();
        controller.Toggle();
        Assert.Equal(2, calls);
    }

    /// <summary>构造控制器（默认视口 10）。</summary>
    private static TranscriptController NewController(IReadOnlyList<string> lines) =>
        new(() => new TranscriptSnapshot(lines, 0), _ => { })
        {
            ViewportHeight = 10,
        };
}
