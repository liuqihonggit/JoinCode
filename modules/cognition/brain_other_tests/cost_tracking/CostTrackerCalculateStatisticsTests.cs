namespace Core.Tests.CostTracking;

/// <summary>
/// CostTracker.CalculateStatistics 内部纯函数测试
/// 改 internal 后直接测统计计算:空记录/单模型/多模型/缓存节省/未知模型/时间区间
/// </summary>
public sealed class CostTrackerCalculateStatisticsTests {
    // ---------- 空记录 ----------

    [Fact]
    public async Task CalculateStatistics_EmptyRecords_ReturnsEmptyStats() {
        await using var tracker = CreateTracker();

        var stats = tracker.CalculateStatistics([]);

        Assert.Equal(0, stats.RequestCount);
        Assert.Equal(0, stats.PromptTokens);
        Assert.Equal(0, stats.CompletionTokens);
        Assert.Equal(0, stats.TotalCostUsd);
        Assert.Empty(stats.ModelBreakdown);
    }

    // ---------- 单模型 ----------

    [Fact]
    public async Task CalculateStatistics_SingleRecord_SumsFields() {
        await using var tracker = CreateTracker();
        var record = MakeRecord("model-a", prompt: 1000, completion: 500, cost: 0.05m);

        var stats = tracker.CalculateStatistics([record]);

        Assert.Equal(1, stats.RequestCount);
        Assert.Equal(1000, stats.PromptTokens);
        Assert.Equal(500, stats.CompletionTokens);
        Assert.Equal(1500, stats.TotalTokens);
        Assert.Equal(0.05m, stats.TotalCostUsd);
    }

    // ---------- 多模型 ----------

    [Fact]
    public async Task CalculateStatistics_MultipleModels_GroupsBreakdown() {
        await using var tracker = CreateTracker();
        var records = new List<TokenUsageRecord> {
            MakeRecord("model-a", prompt: 1000, completion: 500, cost: 0.05m),
            MakeRecord("model-a", prompt: 2000, completion: 1000, cost: 0.10m),
            MakeRecord("model-b", prompt: 500, completion: 200, cost: 0.02m),
        };

        var stats = tracker.CalculateStatistics(records);

        Assert.Equal(3, stats.RequestCount);
        Assert.Equal(3500, stats.PromptTokens);
        Assert.Equal(1700, stats.CompletionTokens);
        Assert.Equal(2, stats.ModelBreakdown.Count);

        var modelA = stats.ModelBreakdown.First(m => m.Model == "model-a");
        Assert.Equal(2, modelA.RequestCount);
        Assert.Equal(3000, modelA.PromptTokens);

        var modelB = stats.ModelBreakdown.First(m => m.Model == "model-b");
        Assert.Equal(1, modelB.RequestCount);
    }

    // ---------- 缓存节省 ----------

    [Fact]
    public async Task CalculateStatistics_CacheReadTokens_ComputesSavings() {
        await using var tracker = CreateTracker();
        tracker.SetModelCost("cached-model", promptCostPer1K: 0.02m, completionCostPer1K: 0.06m);
        var record = MakeRecord("cached-model", prompt: 1000, completion: 0, cost: 0.02m, cacheRead: 1000);

        var stats = tracker.CalculateStatistics([record]);

        // cacheSavings = (1000/1000)*0.02 - (1000/1000)*0.02*0.1 = 0.02 - 0.002 = 0.018
        Assert.True(stats.CacheSavingsUsd > 0);
        Assert.True(Math.Abs(stats.CacheSavingsUsd - 0.018m) < 0.0001m);
    }

    [Fact]
    public async Task CalculateStatistics_NoCacheRead_ZeroSavings() {
        await using var tracker = CreateTracker();
        var record = MakeRecord("model-a", prompt: 1000, completion: 0, cost: 0.01m, cacheRead: 0);

        var stats = tracker.CalculateStatistics([record]);

        Assert.Equal(0, stats.CacheSavingsUsd);
    }

    // ---------- 未知模型 ----------

    [Fact]
    public async Task CalculateStatistics_UnknownModel_FlagsHasUnknownModelCost() {
        await using var tracker = CreateTracker();
        var record = MakeRecord("completely-unknown-model-xyz", prompt: 1000, completion: 500, cost: 0.04m);

        var stats = tracker.CalculateStatistics([record]);

        Assert.True(stats.HasUnknownModelCost);
    }

    // ---------- API 时长与墙钟时长 ----------

    [Fact]
    public async Task CalculateStatistics_ApiDuration_Summed() {
        await using var tracker = CreateTracker();
        var records = new List<TokenUsageRecord> {
            MakeRecord("model-a", cost: 0.01m, apiDurationMs: 100),
            MakeRecord("model-a", cost: 0.01m, apiDurationMs: 200),
        };

        var stats = tracker.CalculateStatistics(records);

        Assert.Equal(TimeSpan.FromMilliseconds(300), stats.ApiDuration);
    }

    [Fact]
    public async Task CalculateStatistics_WallDuration_LastMinusFirst() {
        await using var tracker = CreateTracker();
        var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);
        var records = new List<TokenUsageRecord> {
            MakeRecord("model-a", cost: 0.01m, timestamp: t1),
            MakeRecord("model-a", cost: 0.01m, timestamp: t2),
        };

        var stats = tracker.CalculateStatistics(records);

        Assert.Equal(TimeSpan.FromMinutes(5), stats.WallDuration);
    }

    [Fact]
    public async Task CalculateStatistics_SingleRecord_ZeroWallDuration() {
        await using var tracker = CreateTracker();
        var record = MakeRecord("model-a", cost: 0.01m);

        var stats = tracker.CalculateStatistics([record]);

        Assert.Equal(TimeSpan.Zero, stats.WallDuration);
    }

    // ---------- 缓存 Token 累计 ----------

    [Fact]
    public async Task CalculateStatistics_CacheTokens_Summed() {
        await using var tracker = CreateTracker();
        var records = new List<TokenUsageRecord> {
            MakeRecord("model-a", cost: 0.01m, cacheCreation: 100, cacheRead: 200),
            MakeRecord("model-a", cost: 0.01m, cacheCreation: 50, cacheRead: 80),
        };

        var stats = tracker.CalculateStatistics(records);

        Assert.Equal(150, stats.CacheCreationTokens);
        Assert.Equal(280, stats.CacheReadTokens);
    }

    // ---------- Helpers ----------

    private static CostTracker CreateTracker() {
        var fileOp = new Mock<IFileOperationService>();
        return new CostTracker(
            fileOp.Object,
            storagePath: null,
            NullLogger<CostTracker>.Instance,
            modelConfigLoader: TestModelConfigLoaderFactory.CreateWithDefaultPricing());
    }

    private static TokenUsageRecord MakeRecord(
        string model,
        int prompt = 0,
        int completion = 0,
        decimal cost = 0,
        int cacheCreation = 0,
        int cacheRead = 0,
        double apiDurationMs = 0,
        DateTime? timestamp = null)
        => new() {
            Timestamp = timestamp ?? DateTime.UtcNow,
            Model = model,
            PromptTokens = prompt,
            CompletionTokens = completion,
            CacheCreationTokens = cacheCreation,
            CacheReadTokens = cacheRead,
            CostUsd = cost,
            SessionId = "test-session",
            ApiDurationMs = apiDurationMs
        };
}
