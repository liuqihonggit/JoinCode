// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003

namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// GAP-041-05 主动加热冷工具单元测试 — 验证 BoostToolAsync / HeatColdToolsAsync / boost TTL / GetEffectiveScore 含 boost
/// </summary>
public sealed class ToolHealthMonitorBoostTest : IAsyncLifetime {
    private InMemoryFileSystem _fs = null!;
    private ToolHealthMonitor _monitor = null!;

    public Task InitializeAsync() {
        _fs = new InMemoryFileSystem();
        _monitor = new ToolHealthMonitor(_fs, config: new ToolScoreConfig {
            SuccessDelta = 1, FailDelta = -5, WarningThreshold = 3,
            ScoreMin = -100, ScoreMax = 100,
            DecayRatePerHour = 0.1, DecayRecoveryScore = 1
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync() {
        _monitor.DisposeSafe();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task BoostToolAsync_SetsBoostScoreAndExpiry() {
        var record = await _monitor.BoostToolAsync("cold_tool", 30, TimeSpan.FromHours(1));
        record.BoostScore.Should().Be(30);
        record.BoostExpiry.Should().NotBeNull();
    }

    [Fact]
    public async Task GetEffectiveScore_IncludesActiveBoost() {
        await _monitor.RecordFailureAsync("cold_tool", "err");
        await _monitor.RecordFailureAsync("cold_tool", "err");
        await _monitor.RecordFailureAsync("cold_tool", "err");

        var scoreBefore = _monitor.GetEffectiveScore("cold_tool");
        await _monitor.BoostToolAsync("cold_tool", 30, TimeSpan.FromHours(1));
        var scoreAfter = _monitor.GetEffectiveScore("cold_tool");

        scoreAfter.Should().Be(scoreBefore + 30);
    }

    [Fact]
    public async Task GetEffectiveScore_ExcludesExpiredBoost() {
        await _monitor.BoostToolAsync("cold_tool", 30, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        var score = _monitor.GetEffectiveScore("cold_tool");
        score.Should().Be(0);
    }

    [Fact]
    public async Task IsBoostActive_TrueWhenBoostNotExpired() {
        var record = await _monitor.BoostToolAsync("tool_a", 20, TimeSpan.FromHours(1));
        record.IsBoostActive.Should().BeTrue();
    }

    [Fact]
    public async Task IsBoostActive_FalseWhenBoostExpired() {
        var record = await _monitor.BoostToolAsync("tool_a", 20, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        var fresh = await _monitor.GetRecordAsync("tool_a");
        fresh!.IsBoostActive.Should().BeFalse();
    }

    [Fact]
    public async Task IsBoostActive_FalseWhenNoBoost() {
        await _monitor.RecordSuccessAsync("tool_a");
        var record = await _monitor.GetRecordAsync("tool_a");
        record!.IsBoostActive.Should().BeFalse();
    }

    [Fact]
    public async Task HeatColdToolsAsync_HeatsToolsBelowThreshold() {
        for (var i = 0; i < 5; i++)
            await _monitor.RecordFailureAsync("very_cold", "err");
        for (var i = 0; i < 2; i++)
            await _monitor.RecordFailureAsync("mild", "err");

        var heatedCount = await _monitor.HeatColdToolsAsync(coldThreshold: -15, boostScore: 30, ttl: TimeSpan.FromHours(1));

        heatedCount.Should().Be(1);
        var veryCold = await _monitor.GetRecordAsync("very_cold");
        veryCold!.BoostScore.Should().Be(30);
        var mild = await _monitor.GetRecordAsync("mild");
        mild!.BoostScore.Should().Be(0);
    }

    [Fact]
    public async Task HeatColdToolsAsync_SkipsAlreadyBoostedTools() {
        for (var i = 0; i < 5; i++)
            await _monitor.RecordFailureAsync("cold_a", "err");
        for (var i = 0; i < 5; i++)
            await _monitor.RecordFailureAsync("cold_b", "err");

        await _monitor.BoostToolAsync("cold_a", 30, TimeSpan.FromHours(1));
        var heatedCount = await _monitor.HeatColdToolsAsync(coldThreshold: -15, boostScore: 30, ttl: TimeSpan.FromHours(1));

        heatedCount.Should().Be(1);
    }

    [Fact]
    public async Task HeatColdToolsAsync_ReturnsZeroWhenNoColdTools() {
        await _monitor.RecordSuccessAsync("warm_tool");
        var heatedCount = await _monitor.HeatColdToolsAsync(coldThreshold: -10, boostScore: 30);
        heatedCount.Should().Be(0);
    }

    [Fact]
    public async Task HeatColdToolsAsync_DefaultParameters_HeatsColdTools() {
        for (var i = 0; i < 10; i++)
            await _monitor.RecordFailureAsync("very_cold", "err");

        var heatedCount = await _monitor.HeatColdToolsAsync();

        heatedCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ApplyTimeDecay_ClearsExpiredBoost() {
        await _monitor.RecordSuccessAsync("tool_a");
        await _monitor.BoostToolAsync("tool_a", 30, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        _monitor.ApplyTimeDecay();

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.BoostScore.Should().Be(0);
        record.BoostExpiry.Should().BeNull();
    }

    [Fact]
    public async Task GetEffectiveScore_BoostClampedToMax() {
        for (var i = 0; i < 50; i++)
            await _monitor.RecordSuccessAsync("hot_tool");
        await _monitor.BoostToolAsync("hot_tool", 200, TimeSpan.FromHours(1));

        var score = _monitor.GetEffectiveScore("hot_tool");
        score.Should().Be(100);
    }
}
