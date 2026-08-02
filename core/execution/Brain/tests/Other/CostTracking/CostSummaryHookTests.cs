namespace Core.Tests.CostTracking;

public class CostSummaryHookTests
{
    private readonly Mock<IFileOperationService> _fileOpMock = new();
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "jcc-test-hook", Guid.NewGuid().ToString("N"));

    private Core.CostTracking.CostTracker CreateCostTracker()
    {
        return new Core.CostTracking.CostTracker(
            _fileOpMock.Object,
            storagePath: Path.Combine(_storagePath, "usage.json"),
            NullLogger<Core.CostTracking.CostTracker>.Instance);
    }

    [Fact]
    public async Task PrintSummaryOnExitAsync_ShouldExecuteWithoutError()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            tracker.RecordUsage("model-a", 100, 50);

            var hook = new CostSummaryHook(tracker, NullLogger<CostSummaryHook>.Instance);

            var act = async () => await hook.PrintSummaryOnExitAsync().ConfigureAwait(true);

            await act.Should().NotThrowAsync().ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task GenerateSummaryAsync_WithUsage_ShouldContainCostInfo()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            tracker.RecordUsage("model-a", 1000, 500);

            var hook = new CostSummaryHook(tracker, NullLogger<CostSummaryHook>.Instance);

            var summary = await hook.GenerateSummaryAsync().ConfigureAwait(true);

            summary.Should().Contain("成本摘要");
            summary.Should().Contain("请求次数: 1");
            summary.Should().Contain("model-a");
        }
    }

    [Fact]
    public async Task GenerateSummaryAsync_NoUsage_ShouldReturnEmptyStats()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            var hook = new CostSummaryHook(tracker, NullLogger<CostSummaryHook>.Instance);

            var summary = await hook.GenerateSummaryAsync().ConfigureAwait(true);

            summary.Should().Contain("成本摘要");
            summary.Should().Contain("请求次数: 0");
        }
    }

    [Fact]
    public async Task GenerateSummaryAsync_MultipleModels_ShouldListAllModels()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            tracker.RecordUsage("model-a", 100, 50);
            tracker.RecordUsage("model-b", 200, 100);

            var hook = new CostSummaryHook(tracker, NullLogger<CostSummaryHook>.Instance);

            var summary = await hook.GenerateSummaryAsync().ConfigureAwait(true);

            summary.Should().Contain("model-a");
            summary.Should().Contain("model-b");
            summary.Should().Contain("按模型分类");
        }
    }

    [Fact]
    public async Task GenerateSummaryAsync_WithCacheTokens_ShouldShowCacheInfo()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            tracker.RecordUsage("model-a", 100, 50, 500, 200);

            var hook = new CostSummaryHook(tracker, NullLogger<CostSummaryHook>.Instance);

            var summary = await hook.GenerateSummaryAsync().ConfigureAwait(true);

            summary.Should().Contain("缓存创建Token");
            summary.Should().Contain("缓存读取Token");
        }
    }
}
