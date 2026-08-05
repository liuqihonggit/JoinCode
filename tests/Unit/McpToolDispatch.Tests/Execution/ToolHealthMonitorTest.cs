namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// ToolHealthMonitor 单元测试 — 验证评分增减、提示词阈值、时间衰减、重置、黑名单、降权
/// </summary>
public sealed class ToolHealthMonitorTest : IAsyncLifetime
{
    private InMemoryFileSystem _fs = null!;
    private ToolHealthMonitor _monitor = null!;
    private ToolHealthMonitor _monitorWithBlacklist = null!;

    public Task InitializeAsync()
    {
        _fs = new InMemoryFileSystem();
        _monitor = new ToolHealthMonitor(_fs, config: new ToolScoreConfig
        {
            SuccessDelta = 1,
            FailDelta = -5,
            WarningThreshold = 3,
            ScoreMin = -100,
            ScoreMax = 100,
            DecayRatePerHour = 0.1,
            DecayRecoveryScore = 1
        });
        _monitorWithBlacklist = new ToolHealthMonitor(_fs, config: new ToolScoreConfig(),
            blacklist: new HashSet<string>(["blacklisted_tool"], StringComparer.OrdinalIgnoreCase),
            penalties: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["penalized_tool"] = -20 });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _monitor.Dispose();
        _monitorWithBlacklist.Dispose();
        return Task.CompletedTask;
    }

    // === RecordSuccessAsync ===

    [Fact]
    public async Task RecordSuccessAsync_IncrementsScoreBySuccessDelta()
    {
        var record = await _monitor.RecordSuccessAsync("tool_a");
        record.Score.Should().Be(1);
        record.SuccessCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordSuccessAsync_MultipleSuccesses_ScoreAccumulates()
    {
        await _monitor.RecordSuccessAsync("tool_a");
        await _monitor.RecordSuccessAsync("tool_a");
        await _monitor.RecordSuccessAsync("tool_a");

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.Score.Should().Be(3);
        record.SuccessCount.Should().Be(3);
    }

    [Fact]
    public async Task RecordSuccessAsync_ResetsConsecutiveFailures()
    {
        await _monitor.RecordFailureAsync("tool_a", "err");
        await _monitor.RecordFailureAsync("tool_a", "err");

        var record = await _monitor.RecordSuccessAsync("tool_a");
        record.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task RecordSuccessAsync_ClearsLastErrorMessage()
    {
        await _monitor.RecordFailureAsync("tool_a", "some error");
        var record = await _monitor.RecordSuccessAsync("tool_a");
        record.LastErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task RecordSuccessAsync_ScoreClampedToMax()
    {
        for (var i = 0; i < 150; i++)
            await _monitor.RecordSuccessAsync("tool_a");

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.Score.Should().Be(100);
    }

    // === RecordFailureAsync ===

    [Fact]
    public async Task RecordFailureAsync_DecrementsScoreByFailDelta()
    {
        var record = await _monitor.RecordFailureAsync("tool_a", "timeout");
        record.Score.Should().Be(-5);
        record.FailCount.Should().Be(1);
        record.ConsecutiveFailures.Should().Be(1);
        record.LastErrorMessage.Should().Be("timeout");
    }

    [Fact]
    public async Task RecordFailureAsync_MultipleFailures_ScoreAccumulates()
    {
        await _monitor.RecordFailureAsync("tool_a", "err1");
        await _monitor.RecordFailureAsync("tool_a", "err2");

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.Score.Should().Be(-10);
        record.FailCount.Should().Be(2);
        record.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public async Task RecordFailureAsync_ScoreClampedToMin()
    {
        for (var i = 0; i < 30; i++)
            await _monitor.RecordFailureAsync("tool_a", "err");

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.Score.Should().Be(-100);
    }

    // === 提示词阈值 ===

    [Fact]
    public async Task RecordFailureAsync_ConsecutiveFailuresReachesThreshold_StaysEnabled()
    {
        await _monitor.RecordFailureAsync("tool_a", "err1");
        await _monitor.RecordFailureAsync("tool_a", "err2");
        (await _monitor.GetRecordAsync("tool_a"))!.IsEnabled.Should().BeTrue();

        var record = await _monitor.RecordFailureAsync("tool_a", "err3");
        record.ConsecutiveFailures.Should().Be(3);
        record.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task RecordFailureAsync_SuccessBetweenFailures_ResetsConsecutiveCount()
    {
        await _monitor.RecordFailureAsync("tool_a", "err1");
        await _monitor.RecordFailureAsync("tool_a", "err2");
        await _monitor.RecordSuccessAsync("tool_a");
        await _monitor.RecordFailureAsync("tool_a", "err3");

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.ConsecutiveFailures.Should().Be(1);
        record.IsEnabled.Should().BeTrue();
    }

    // === 评分恢复 ===

    [Fact]
    public async Task RecordSuccessAsync_ScoreRecoversAfterFailures()
    {
        await _monitor.RecordFailureAsync("tool_a", "err1");
        await _monitor.RecordFailureAsync("tool_a", "err2");
        await _monitor.RecordFailureAsync("tool_a", "err3");
        (await _monitor.GetRecordAsync("tool_a"))!.IsEnabled.Should().BeTrue();

        for (var i = 0; i < 20; i++)
            await _monitor.RecordSuccessAsync("tool_a");

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.IsEnabled.Should().BeTrue();
        record.Score.Should().BeGreaterThan(-50);
    }

    // === ResetToolAsync ===

    [Fact]
    public async Task ResetToolAsync_ResetsScoreAndConsecutiveFailures()
    {
        await _monitor.RecordFailureAsync("tool_a", "err1");
        await _monitor.RecordFailureAsync("tool_a", "err2");
        await _monitor.RecordFailureAsync("tool_a", "err3");
        (await _monitor.GetRecordAsync("tool_a"))!.IsEnabled.Should().BeTrue();

        await _monitor.ResetToolAsync("tool_a");

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.Score.Should().Be(0);
        record.ConsecutiveFailures.Should().Be(0);
        record.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ResetToolAsync_NonExistentTool_DoesNotThrow()
    {
        var act = async () => await _monitor.ResetToolAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    // === GetRecordAsync ===

    [Fact]
    public async Task GetRecordAsync_NonExistentTool_ReturnsNull()
    {
        var record = await _monitor.GetRecordAsync("nonexistent");
        record.Should().BeNull();
    }

    // === GetAllRecordsAsync ===

    [Fact]
    public async Task GetAllRecordsAsync_ReturnsAllRecords()
    {
        await _monitor.RecordSuccessAsync("tool_a");
        await _monitor.RecordFailureAsync("tool_b", "err");

        var all = await _monitor.GetAllRecordsAsync();
        all.Count.Should().Be(2);
        all.Should().ContainKey("tool_a");
        all.Should().ContainKey("tool_b");
    }

    // === 持久化 ===

    [Fact]
    public async Task RecordSuccessAsync_PersistsToDisk()
    {
        await _monitor.RecordSuccessAsync("tool_a");
        _monitor.Dispose();

        var monitor2 = new ToolHealthMonitor(_fs, config: new ToolScoreConfig());
        try
        {
            var record = await monitor2.GetRecordAsync("tool_a");
            record.Should().NotBeNull();
            record!.Score.Should().Be(1);
        }
        finally
        {
            monitor2.Dispose();
        }
    }

    // === SuccessRate ===

    [Fact]
    public async Task SuccessRate_MixedResults_CalculatesCorrectly()
    {
        await _monitor.RecordSuccessAsync("tool_a");
        await _monitor.RecordSuccessAsync("tool_a");
        await _monitor.RecordFailureAsync("tool_a", "err");

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.SuccessRate.Should().BeApproximately(2.0 / 3.0, 0.001);
    }

    [Fact]
    public async Task SuccessRate_OnlySuccess_ReturnsOne()
    {
        await _monitor.RecordSuccessAsync("tool_a");
        var record = await _monitor.GetRecordAsync("tool_a");
        record!.SuccessRate.Should().Be(1.0);
    }

    // === 黑名单 ===

    [Fact]
    public void IsBlacklisted_BlacklistedTool_ReturnsTrue()
    {
        _monitorWithBlacklist.IsBlacklisted("blacklisted_tool").Should().BeTrue();
    }

    [Fact]
    public void IsBlacklisted_NormalTool_ReturnsFalse()
    {
        _monitorWithBlacklist.IsBlacklisted("normal_tool").Should().BeFalse();
    }

    // === 降权 ===

    [Fact]
    public void GetPenalty_PenalizedTool_ReturnsPenalty()
    {
        _monitorWithBlacklist.GetPenalty("penalized_tool").Should().Be(-20);
    }

    [Fact]
    public void GetPenalty_NormalTool_ReturnsZero()
    {
        _monitorWithBlacklist.GetPenalty("normal_tool").Should().Be(0);
    }

    // === 有效评分 ===

    [Fact]
    public async Task GetEffectiveScore_WithPenalty_ReturnsAdjustedScore()
    {
        await _monitorWithBlacklist.RecordSuccessAsync("penalized_tool");
        _monitorWithBlacklist.GetEffectiveScore("penalized_tool").Should().Be(-19);
    }

    [Fact]
    public void GetEffectiveScore_BlacklistedTool_ReturnsMinScore()
    {
        _monitorWithBlacklist.GetEffectiveScore("blacklisted_tool").Should().Be(-100);
    }

    [Fact]
    public async Task GetEffectiveScore_NormalTool_ReturnsBaseScore()
    {
        await _monitorWithBlacklist.RecordSuccessAsync("normal_tool");
        _monitorWithBlacklist.GetEffectiveScore("normal_tool").Should().Be(1);
    }

    // === 通配符黑名单 ===

    [Fact]
    public void IsBlacklisted_WildcardPattern_MatchesToolName()
    {
        var fs = new InMemoryFileSystem();
        using var monitor = new ToolHealthMonitor(fs,
            blacklist: new HashSet<string>(["shell_*"], StringComparer.OrdinalIgnoreCase));

        monitor.IsBlacklisted("shell_check").Should().BeTrue();
        monitor.IsBlacklisted("shell_background_get").Should().BeTrue();
        monitor.IsBlacklisted("Bash").Should().BeFalse();
    }

    [Fact]
    public void IsBlacklisted_WildcardPrefixAndSuffix_MatchesToolName()
    {
        var fs = new InMemoryFileSystem();
        using var monitor = new ToolHealthMonitor(fs,
            blacklist: new HashSet<string>(["*_background_*"], StringComparer.OrdinalIgnoreCase));

        monitor.IsBlacklisted("shell_background_get").Should().BeTrue();
        monitor.IsBlacklisted("shell_background_list").Should().BeTrue();
        monitor.IsBlacklisted("shell_check").Should().BeFalse();
    }

    // === 通配符降权 ===

    [Fact]
    public void GetPenalty_WildcardPattern_MatchesToolName()
    {
        var fs = new InMemoryFileSystem();
        using var monitor = new ToolHealthMonitor(fs,
            penalties: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["shell_*"] = -30 });

        monitor.GetPenalty("shell_check").Should().Be(-30);
        monitor.GetPenalty("shell_background_get").Should().Be(-30);
        monitor.GetPenalty("Bash").Should().Be(0);
    }
}
