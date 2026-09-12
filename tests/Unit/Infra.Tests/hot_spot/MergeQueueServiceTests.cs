namespace Infra.Tests.HotSpot;


public sealed class MergeQueueServiceTests
{
    private static MergeQueueItem MakeItem(string workerId = "w1", string branch = "worktree-w1") =>
        new() { WorkerId = workerId, WorktreeBranch = branch, TaskId = "T1", EnqueuedAt = DateTimeOffset.UtcNow };

    private static IMergeQueueService CreateSut(
        out Func<string, CancellationToken, Task<bool>> compileValidator,
        out Func<string, CancellationToken, Task<bool>> mergeExecutor)
    {
        compileValidator = (_, _) => Task.FromResult(true);
        mergeExecutor = (_, _) => Task.FromResult(true);
        return new MergeQueueService(compileValidator, mergeExecutor);
    }

    [Fact]
    public async Task Enqueue_ShouldIncreasePendingCount()
    {
        var sut = CreateSut(out _, out _);
        await sut.EnqueueAsync(MakeItem());
        sut.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessNext_EmptyQueue_ShouldReturnEmpty()
    {
        var sut = CreateSut(out _, out _);
        var result = await sut.ProcessNextAsync();
        result.Should().BeEquivalentTo(MergeResult.Empty());
    }

    [Fact]
    public async Task ProcessNext_CompileAndMergeOk_ShouldReturnSuccess()
    {
        var sut = new MergeQueueService(
            (_, _) => Task.FromResult(true),
            (_, _) => Task.FromResult(true));
        await sut.EnqueueAsync(MakeItem("w1", "branch-1"));

        var result = await sut.ProcessNextAsync();

        result.Success.Should().BeTrue();
        result.MergedBranch.Should().Be("branch-1");
        sut.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessNext_CompileFailed_ShouldReturnCompileFailed()
    {
        var sut = new MergeQueueService(
            (_, _) => Task.FromResult(false),
            (_, _) => Task.FromResult(true));
        await sut.EnqueueAsync(MakeItem("w1", "bad-branch"));

        var result = await sut.ProcessNextAsync();

        result.Success.Should().BeFalse();
        result.FailedWorkerId.Should().Be("w1");
        result.Message.Should().Contain("编译");
    }

    [Fact]
    public async Task ProcessNext_MergeFailed_ShouldReturnMergeFailed()
    {
        var sut = new MergeQueueService(
            (_, _) => Task.FromResult(true),
            (_, _) => Task.FromResult(false));
        await sut.EnqueueAsync(MakeItem("w1", "conflict-branch"));

        var result = await sut.ProcessNextAsync();

        result.Success.Should().BeFalse();
        result.FailedWorkerId.Should().Be("w1");
        result.Message.Should().Contain("合并");
    }

    [Fact]
    public async Task ProcessNext_Sequential_ShouldProcessInOrder()
    {
        var sut = new MergeQueueService(
            (_, _) => Task.FromResult(true),
            (_, _) => Task.FromResult(true));
        await sut.EnqueueAsync(MakeItem("w1", "b1"));
        await sut.EnqueueAsync(MakeItem("w2", "b2"));
        await sut.EnqueueAsync(MakeItem("w3", "b3"));

        var r1 = await sut.ProcessNextAsync();
        var r2 = await sut.ProcessNextAsync();
        var r3 = await sut.ProcessNextAsync();
        var r4 = await sut.ProcessNextAsync();

        r1.MergedBranch.Should().Be("b1");
        r2.MergedBranch.Should().Be("b2");
        r3.MergedBranch.Should().Be("b3");
        r4.Success.Should().BeTrue("空队列返回Empty成功");
    }

    [Fact]
    public async Task GetPending_ShouldReturnAllQueuedItems()
    {
        var sut = CreateSut(out _, out _);
        await sut.EnqueueAsync(MakeItem("w1", "b1"));
        await sut.EnqueueAsync(MakeItem("w2", "b2"));

        var pending = sut.GetPending();
        pending.Should().HaveCount(2);
        pending.Select(x => x.WorkerId).Should().BeEquivalentTo(["w1", "w2"]);
    }
}
