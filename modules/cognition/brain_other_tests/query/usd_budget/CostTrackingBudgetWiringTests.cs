namespace Core.Tests.UsdBudget;

/// <summary>
/// TASK071 query2-①/② — 成本追踪 → USD 预算接线测试：
/// ICostTracker 真实计价优先（NullTracker 回退时 Complete CostUsd 恒 0 的旧缺陷）；
/// TrackCostAsync 增量记录到 IUsdBudgetManager（RecordCostAsync 旧实现全仓零调用 → 预算熔断失效）。
/// </summary>
public sealed class CostTrackingBudgetWiringTests {
    /// <summary>可控累计成本的假会话追踪器。</summary>
    private sealed class FakeSessionCostTracker : ICostTracker {
        public decimal TotalCost { get; set; }

        public CostStatistics GetTotalStatistics() => new() { TotalCostUsd = TotalCost };
        public CostStatistics GetSessionStatistics(string sessionId) => new() { TotalCostUsd = TotalCost };
        public CostStatistics GetTodayStatistics() => new() { TotalCostUsd = TotalCost };
        public void RecordUsage(string model, int promptTokens, int completionTokens, string? sessionId = null) { }
        public void RecordUsage(string model, int promptTokens, int completionTokens, int cacheCreationTokens, int cacheReadTokens, string? sessionId = null) { }
        public void RecordUsage(string model, int promptTokens, int completionTokens, int cacheCreationTokens, int cacheReadTokens, double apiDurationMs, string? sessionId = null) { }
        public void RecordLinesChanged(int added, int removed) { }
        public void Reset() { }
    }

    /// <summary>捕获增量记录的假预算管理器。</summary>
    private sealed class FakeBudgetManager : IUsdBudgetManager {
        public List<(decimal Cost, string Reason)> Recorded { get; } = [];
        public bool Exceeded { get; set; }

        public Task<bool> IsBudgetExceededAsync(CancellationToken ct = default) => Task.FromResult(Exceeded);
        public Task<UsdBudgetStatus> GetBudgetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new UsdBudgetStatus {
                MaxBudget = 10m,
                TotalUsed = 0m,
                Remaining = 10m,
                UsagePercentage = 0,
                IsExceeded = false,
            });
        public Task RecordCostAsync(decimal costUsd, string reason, CancellationToken ct = default) {
            Recorded.Add((costUsd, reason));
            return Task.CompletedTask;
        }
#pragma warning disable CS0067 // fake 不触发事件
        public event EventHandler<UsdBudgetAlertEventArgs>? BudgetAlert;
#pragma warning restore CS0067
    }

    private static QueryMiddlewareContext CreateContext() => new() {
        UserInput = "test",
        ChatHistory = new MessageList(),
        Config = new QueryEngineConfig(),
        Kernel = new Mock<IChatClient>().Object,
        ToolRegistry = new Mock<IToolRegistry>().Object,
        InputTokens = 100,
        OutputTokens = 50,
    };

    [Fact]
    public async Task TrackCostAsync_UsesRealSessionCostTracker_NotNullFallback() {
        // TASK071 query2-② — ICostTracker 权威口径优先（旧实现恒 NullTracker → CostUsd 恒 0）
        var sessionTracker = new FakeSessionCostTracker { TotalCost = 0.42m };
        await using var middleware = new CostTrackingMiddleware(
            sessionCostTracker: sessionTracker,
            usdBudgetManager: new FakeBudgetManager());
        var context = CreateContext();

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        foreach (var hook in context.AfterLlmCallHooks) {
            await hook(context, CancellationToken.None).ConfigureAwait(true);
        }

        context.TotalCostUsd.Should().Be(0.42m, "真实计价应写入 Complete chunk 的成本快照");
    }

    [Fact]
    public async Task TrackCostAsync_RecordsDeltaToBudgetManager() {
        // TASK071 query2-① — 增量记录（total - 上次快照）驱动预算熔断
        var sessionTracker = new FakeSessionCostTracker { TotalCost = 0.10m };
        var budget = new FakeBudgetManager();
        await using var middleware = new CostTrackingMiddleware(
            sessionCostTracker: sessionTracker,
            usdBudgetManager: budget);
        var context = CreateContext();

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        foreach (var hook in context.AfterLlmCallHooks) {
            await hook(context, CancellationToken.None).ConfigureAwait(true);
        }

        budget.Recorded.Should().ContainSingle("首次调用应记录全部增量");
        budget.Recorded[0].Cost.Should().Be(0.10m);

        // 第二次调用成本上涨 → 只记增量
        sessionTracker.TotalCost = 0.35m;
        foreach (var hook in context.AfterLlmCallHooks) {
            await hook(context, CancellationToken.None).ConfigureAwait(true);
        }

        budget.Recorded.Should().HaveCount(2);
        budget.Recorded[1].Cost.Should().Be(0.25m, "增量 = 新累计 − 上次快照（防双计）");
        context.TotalCostUsd.Should().Be(0.35m);
    }

    [Fact]
    public async Task TrackCostAsync_NoCostChange_DoesNotRecord() {
        var sessionTracker = new FakeSessionCostTracker { TotalCost = 0.10m };
        var budget = new FakeBudgetManager();
        await using var middleware = new CostTrackingMiddleware(
            sessionCostTracker: sessionTracker,
            usdBudgetManager: budget);
        var context = CreateContext();

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        foreach (var hook in context.AfterLlmCallHooks) {
            await hook(context, CancellationToken.None).ConfigureAwait(true);
        }
        var countAfterFirst = budget.Recorded.Count;

        // 成本未变（缓存命中/无新调用）→ 不重复记录
        foreach (var hook in context.AfterLlmCallHooks) {
            await hook(context, CancellationToken.None).ConfigureAwait(true);
        }

        budget.Recorded.Count.Should().Be(countAfterFirst, "delta ≤ 0 不记录");
    }
}
