namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// ToolHealthMonitor 单元测试 — 验证评分增减、熔断、时间衰减、重置、自动恢复
/// </summary>
public sealed class ToolHealthMonitorTest : IAsyncLifetime
{
    private InMemoryFileSystem _fs = null!;
    private ToolHealthMonitor _monitor = null!;

    public Task InitializeAsync()
    {
        _fs = new InMemoryFileSystem();
        _monitor = new ToolHealthMonitor(_fs, config: new ToolScoreConfig
        {
            SuccessDelta = 1,
            FailDelta = -5,
            CircuitBreakerThreshold = 3,
            ScoreMin = -100,
            ScoreMax = 100,
            DecayRatePerHour = 0.1,
            DecayRecoveryScore = 1
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _monitor.Dispose();
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

    // === 熔断 ===

    [Fact]
    public async Task RecordFailureAsync_ConsecutiveFailuresReachesThreshold_DisablesTool()
    {
        await _monitor.RecordFailureAsync("tool_a", "err1");
        await _monitor.RecordFailureAsync("tool_a", "err2");
        (await _monitor.GetRecordAsync("tool_a"))!.IsEnabled.Should().BeTrue();

        // 第3次连续失败 → 熔断
        var record = await _monitor.RecordFailureAsync("tool_a", "err3");
        record.IsEnabled.Should().BeFalse();
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

    // === 自动恢复 ===

    [Fact]
    public async Task RecordSuccessAsync_AutoReEnablesWhenScoreRecovers()
    {
        await _monitor.RecordFailureAsync("tool_a", "err1");
        await _monitor.RecordFailureAsync("tool_a", "err2");
        await _monitor.RecordFailureAsync("tool_a", "err3");
        (await _monitor.GetRecordAsync("tool_a"))!.IsEnabled.Should().BeFalse();

        for (var i = 0; i < 20; i++)
            await _monitor.RecordSuccessAsync("tool_a");

        var record = await _monitor.GetRecordAsync("tool_a");
        record!.IsEnabled.Should().BeTrue();
        record.Score.Should().BeGreaterThan(-50);
    }

    // === ResetToolAsync ===

    [Fact]
    public async Task ResetToolAsync_ResetsScoreAndReEnables()
    {
        await _monitor.RecordFailureAsync("tool_a", "err1");
        await _monitor.RecordFailureAsync("tool_a", "err2");
        await _monitor.RecordFailureAsync("tool_a", "err3");
        (await _monitor.GetRecordAsync("tool_a"))!.IsEnabled.Should().BeFalse();

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
}
