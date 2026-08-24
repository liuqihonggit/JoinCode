namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// ChunkPlanner 单元测试 — 验证分片规划:单分片/多分片/不能整除/钳制/连续性/边界
/// </summary>
public sealed class ChunkPlannerTests
{
    private const long OneMb = 1024 * 1024;
    private const long SixteenMb = 16 * OneMb;

    // === 单线程 ===

    [Fact]
    public void Plan_SingleThread_ReturnsOneChunkCoveringAll()
    {
        var chunks = ChunkPlanner.Plan(10 * OneMb, maxThreads: 1);
        chunks.Should().HaveCount(1);
        chunks[0].Index.Should().Be(0);
        chunks[0].Start.Should().Be(0);
        chunks[0].End.Should().Be(10 * OneMb - 1);
        chunks[0].Length.Should().Be(10 * OneMb);
    }

    // === 多线程 ===

    [Fact]
    public void Plan_MultiThread_ChunkCountMatchesThreads()
    {
        var chunks = ChunkPlanner.Plan(8 * OneMb, maxThreads: 4);
        chunks.Should().HaveCount(4);
    }

    [Fact]
    public void Plan_MultiThread_EachChunkSizeEqualsAuto()
    {
        var chunks = ChunkPlanner.Plan(8 * OneMb, maxThreads: 4);
        var expectedSize = 2 * OneMb;
        foreach (var c in chunks)
            c.Length.Should().Be(expectedSize);
    }

    // === 不能整除:最后分片包含余数 ===

    [Fact]
    public void Plan_NotDivisible_LastChunkHasRemainder()
    {
        var total = 10 * OneMb + 100;
        var chunks = ChunkPlanner.Plan(total, maxThreads: 4, chunkSize: 2 * OneMb);

        chunks.Should().HaveCount(6);
        chunks[^1].End.Should().Be(total - 1);

        var sum = chunks.Sum(c => c.Length);
        sum.Should().Be(total);
    }

    // === 钳制:自动分片大小最小 1MB ===

    [Fact]
    public void Plan_AutoSize_ClampedToMin1Mb()
    {
        var chunks = ChunkPlanner.Plan(2 * OneMb, maxThreads: 8);
        var minSize = chunks.Min(c => c.Length);
        minSize.Should().BeGreaterThanOrEqualTo(OneMb, "自动分片大小应钳制到最小 1MB");
    }

    // === 钳制:自动分片大小最大 16MB ===

    [Fact]
    public void Plan_AutoSize_ClampedToMax16Mb()
    {
        var chunks = ChunkPlanner.Plan(1024 * OneMb, maxThreads: 4);
        var maxSize = chunks.Max(c => c.Length);
        maxSize.Should().BeLessThanOrEqualTo(SixteenMb, "自动分片大小应钳制到最大 16MB");
    }

    // === 指定分片大小 ===

    [Fact]
    public void Plan_ExplicitChunkSize_SplitsByGivenSize()
    {
        var chunks = ChunkPlanner.Plan(5 * OneMb, maxThreads: 2, chunkSize: OneMb);
        chunks.Should().HaveCount(5);
        foreach (var c in chunks)
            c.Length.Should().Be(OneMb);
    }

    // === 连续无间隙无重叠 ===

    [Fact]
    public void Plan_ChunksAreContiguous_NoGapNoOverlap()
    {
        var chunks = ChunkPlanner.Plan(7 * OneMb + 333, maxThreads: 3, chunkSize: OneMb);

        chunks[0].Start.Should().Be(0);
        for (var i = 1; i < chunks.Count; i++)
            chunks[i].Start.Should().Be(chunks[i - 1].End + 1, "分片应连续无间隙");

        var indices = chunks.Select(c => c.Index).ToArray();
        indices.Should().BeInAscendingOrder();
        indices.Should().BeEquivalentTo(Enumerable.Range(0, chunks.Count));
    }

    // === 总字节覆盖 ===

    [Fact]
    public void Plan_SumOfChunkLengths_EqualsContentLength()
    {
        var total = 13 * OneMb + 777;
        var chunks = ChunkPlanner.Plan(total, maxThreads: 5);
        chunks.Sum(c => c.Length).Should().Be(total);
    }

    // === 边界:抛异常 ===

    [Fact]
    public void Plan_ZeroContentLength_Throws()
    {
        var act = () => ChunkPlanner.Plan(0, maxThreads: 1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*[DOWN007]*");
    }

    [Fact]
    public void Plan_NegativeContentLength_Throws()
    {
        var act = () => ChunkPlanner.Plan(-1, maxThreads: 1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*[DOWN007]*");
    }

    [Fact]
    public void Plan_ZeroMaxThreads_Throws()
    {
        var act = () => ChunkPlanner.Plan(OneMb, maxThreads: 0);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*[DOWN003]*");
    }

    [Fact]
    public void Plan_ZeroChunkSize_Throws()
    {
        var act = () => ChunkPlanner.Plan(OneMb, maxThreads: 2, chunkSize: 0);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*[DOWN004]*");
    }

    // === 小文件:多线程也只产生少量分片 ===

    [Fact]
    public void Plan_SmallFile_MultiThread_FewChunks()
    {
        var chunks = ChunkPlanner.Plan(500 * 1024, maxThreads: 8);
        chunks.Should().HaveCount(1, "500KB 小文件按 1MB 最小分片应只有 1 个分片");
    }
}
