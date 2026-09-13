namespace Core.Goal.Tests;


public sealed class GoalConflictMessengerTests
{
    private readonly GoalConflictMessenger _sut = new();

    private static ConflictMessage CreateMessage(string source, string target, string content = "冲突")
        => new() { SourceNodeId = source, TargetNodeId = target, Content = content };

    [Fact]
    public async Task EnqueueConflictAsync_DequeueConflictsAsync_Should_RoundTrip()
    {
        var msg = CreateMessage("node_a", "node_b");

        await _sut.EnqueueConflictAsync(msg).ConfigureAwait(true);
        var result = await _sut.DequeueConflictsAsync("node_b").ConfigureAwait(true);

        Assert.Single(result);
        Assert.Equal("node_a", result[0].SourceNodeId);
        Assert.Equal("node_b", result[0].TargetNodeId);
    }

    [Fact]
    public async Task DequeueConflictsAsync_NoMessages_Should_ReturnEmpty()
    {
        var result = await _sut.DequeueConflictsAsync("unknown_node").ConfigureAwait(true);
        Assert.Empty(result);
    }

    [Fact]
    public async Task DequeueConflictsAsync_Should_ClearQueue()
    {
        await _sut.EnqueueConflictAsync(CreateMessage("a", "target")).ConfigureAwait(true);
        await _sut.EnqueueConflictAsync(CreateMessage("b", "target")).ConfigureAwait(true);

        var first = await _sut.DequeueConflictsAsync("target").ConfigureAwait(true);
        var second = await _sut.DequeueConflictsAsync("target").ConfigureAwait(true);

        Assert.Equal(2, first.Count);
        Assert.Empty(second);
    }

    [Fact]
    public async Task EnqueueConflictAsync_MultipleSources_SameTarget_Should_QueueAll()
    {
        await _sut.EnqueueConflictAsync(CreateMessage("src1", "target", "冲突1")).ConfigureAwait(true);
        await _sut.EnqueueConflictAsync(CreateMessage("src2", "target", "冲突2")).ConfigureAwait(true);
        await _sut.EnqueueConflictAsync(CreateMessage("src3", "target", "冲突3")).ConfigureAwait(true);

        var result = await _sut.DequeueConflictsAsync("target").ConfigureAwait(true);

        Assert.Equal(3, result.Count);
        Assert.Equal("src1", result[0].SourceNodeId);
        Assert.Equal("src2", result[1].SourceNodeId);
        Assert.Equal("src3", result[2].SourceNodeId);
    }

    [Fact]
    public async Task EnqueueConflictAsync_DifferentTargets_Should_IsolateQueues()
    {
        await _sut.EnqueueConflictAsync(CreateMessage("a", "target1")).ConfigureAwait(true);
        await _sut.EnqueueConflictAsync(CreateMessage("b", "target2")).ConfigureAwait(true);

        var r1 = await _sut.DequeueConflictsAsync("target1").ConfigureAwait(true);
        var r2 = await _sut.DequeueConflictsAsync("target2").ConfigureAwait(true);

        Assert.Single(r1);
        Assert.Single(r2);
        Assert.Equal("a", r1[0].SourceNodeId);
        Assert.Equal("b", r2[0].SourceNodeId);
    }

    [Fact]
    public async Task GetPendingCount_Should_ReturnQueueSize()
    {
        await _sut.EnqueueConflictAsync(CreateMessage("a", "target")).ConfigureAwait(true);
        await _sut.EnqueueConflictAsync(CreateMessage("b", "target")).ConfigureAwait(true);

        Assert.Equal(2, _sut.GetPendingCount("target"));
        Assert.Equal(0, _sut.GetPendingCount("unknown"));
    }

    [Fact]
    public async Task EnqueueConflictAsync_NullMessage_Should_Throw()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.EnqueueConflictAsync(null!).AsTask()).ConfigureAwait(true);
    }
}
