// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Core.Tests.Query.BudgetAnalysis;

public class DiminishingReturnsDetectorTests {
    private readonly DiminishingReturnsDetector _detector = new();

    private static List<TokenConsumption> Seq(params int[] amounts) =>
        amounts.Select(a => new TokenConsumption { Amount = a }).ToList();

    [Fact]
    public void CheckDiminishingReturns_InsufficientSamples_ShouldReturnNotDiminishing() {
        var result = _detector.CheckDiminishingReturns(Seq(100), 0);

        result.IsDiminishing.Should().BeFalse();
        result.EffectivenessRatio.Should().Be(1.0);
        result.ConsecutiveLowValueIterations.Should().Be(0);
    }

    [Fact]
    public void CheckDiminishingReturns_EmptyList_ShouldReturnNotDiminishing() {
        var result = _detector.CheckDiminishingReturns([], 0);

        result.IsDiminishing.Should().BeFalse();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void CheckDiminishingReturns_NullInput_ShouldThrowArgumentNullException() {
        var act = () => _detector.CheckDiminishingReturns(null!, 0);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CheckDiminishingReturns_HighRatioData_ShouldNotTriggerDiminishing() {
        var result = _detector.CheckDiminishingReturns(Seq(100, 200, 400, 800), 0);

        result.IsDiminishing.Should().BeFalse();
        result.EffectivenessRatio.Should().BeGreaterThan(0.1);
        result.ConsecutiveLowValueIterations.Should().Be(0, "高比值清零连续计数");
    }

    [Fact]
    public void CheckDiminishingReturns_LowRatioData_ShouldTriggerDiminishing() {
        var consumptions = Seq(1000, 10, 1);
        var count = 0;
        DiminishingReturnsResult result = new();
        for (var i = 0; i < 3; i++) {
            result = _detector.CheckDiminishingReturns(consumptions, count);
            count = result.ConsecutiveLowValueIterations;
        }

        result.IsDiminishing.Should().BeTrue();
        result.ConsecutiveLowValueIterations.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void CheckDiminishingReturns_LowRatioOnce_ShouldNotTriggerYet() {
        var result = _detector.CheckDiminishingReturns(Seq(1000, 10), 0);

        result.IsDiminishing.Should().BeFalse();
        result.ConsecutiveLowValueIterations.Should().Be(1);
    }

    [Fact]
    public void CheckDiminishingReturns_LowRatioTwice_ShouldNotTriggerYet() {
        var first = _detector.CheckDiminishingReturns(Seq(1000, 10), 0);
        var result = _detector.CheckDiminishingReturns(Seq(1000, 10), first.ConsecutiveLowValueIterations);

        result.IsDiminishing.Should().BeFalse();
        result.ConsecutiveLowValueIterations.Should().Be(2);
    }

    [Fact]
    public void CheckDiminishingReturns_HighRatioAfterLow_ShouldResetConsecutiveCount() {
        _detector.CheckDiminishingReturns(Seq(1000, 10), 0);
        var low = _detector.CheckDiminishingReturns(Seq(1000, 10), 1);

        var result = _detector.CheckDiminishingReturns(Seq(100, 500), low.ConsecutiveLowValueIterations);

        result.IsDiminishing.Should().BeFalse();
        result.ConsecutiveLowValueIterations.Should().Be(0);
    }

    [Fact]
    public void CheckDiminishingReturns_ExplicitCount_ShouldNotLeakBetweenQueries() {
        var low = Seq(1000, 10, 1);

        var firstQuery = _detector.CheckDiminishingReturns(low, 0);
        var secondQuery = _detector.CheckDiminishingReturns(low, 0);

        secondQuery.IsDiminishing.Should().BeFalse("检测器无状态：相同入参必得相同结果");
        secondQuery.ConsecutiveLowValueIterations.Should().Be(firstQuery.ConsecutiveLowValueIterations);
    }

    [Fact]
    public void CheckDiminishingReturns_ZeroPreviousAmount_ShouldSkipRatio() {
        var result = _detector.CheckDiminishingReturns(Seq(0, 100), 0);

        result.IsDiminishing.Should().BeFalse();
        result.ConsecutiveLowValueIterations.Should().Be(0);
    }

    [Fact]
    public void CheckDiminishingReturns_SustainedDiminishing_ShouldRecommendStop() {
        var low = Seq(1000, 10, 1);
        var count = 0;
        for (var i = 0; i < 5; i++) {
            count = _detector.CheckDiminishingReturns(low, count).ConsecutiveLowValueIterations;
        }

        var result = _detector.CheckDiminishingReturns(low, count);

        result.IsDiminishing.Should().BeTrue();
        result.Recommendation.Should().Contain("Stop iteration");
    }

    private static QueryMiddlewareContext CreateContext() => new() {
        UserInput = "检查递减回报",
        ChatHistory = [],
        Config = new QueryEngineConfig(),
        Kernel = new Mock<IChatClient>().Object,
        ToolRegistry = new Mock<IToolRegistry>().Object
    };

    [Fact]
    public async Task Middleware_TwoContexts_ShouldKeepIndependentCounts() {
        await using var middleware = new DiminishingReturnsMiddleware(_detector);
        var a = CreateContext();
        var b = CreateContext();
        await middleware.InvokeAsync(a, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        await middleware.InvokeAsync(b, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        var low = Seq(1000, 10, 1);

        // A 连打两次低值，B 只打一次；共享 Singleton 中间件的两个查询不得串状态
        a.RecentConsumptions.Clear();
        a.RecentConsumptions.AddRange(low);
        b.RecentConsumptions.Clear();
        b.RecentConsumptions.AddRange(low);
        await a.AfterToolCallHooks[0](a, CancellationToken.None).ConfigureAwait(true);
        await a.AfterToolCallHooks[0](a, CancellationToken.None).ConfigureAwait(true);
        await b.AfterToolCallHooks[0](b, CancellationToken.None).ConfigureAwait(true);

        a.DiminishingLowValueIterations.Should().Be(2);
        b.DiminishingLowValueIterations.Should().Be(1);
        a.ShouldBreak.Should().BeFalse();
        b.ShouldBreak.Should().BeFalse();
    }

    [Fact]
    public async Task Middleware_ThirdLowValue_ShouldEmitWarningAndBreakOnlyThatContext() {
        await using var middleware = new DiminishingReturnsMiddleware(_detector);
        var a = CreateContext();
        var b = CreateContext();
        await middleware.InvokeAsync(a, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        await middleware.InvokeAsync(b, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        var low = Seq(1000, 10, 1);
        a.RecentConsumptions.AddRange(low);
        b.RecentConsumptions.AddRange(low);

        for (var i = 0; i < 3; i++) {
            await a.AfterToolCallHooks[0](a, CancellationToken.None).ConfigureAwait(true);
        }

        a.ShouldBreak.Should().BeTrue("第三次连续低值触发本查询止血");
        a.OutputChunks.Should().Contain(c => c.Content != null && c.Content.Contains("⚠️"));
        b.ShouldBreak.Should().BeFalse("另一条查询不受影响");
    }
}
