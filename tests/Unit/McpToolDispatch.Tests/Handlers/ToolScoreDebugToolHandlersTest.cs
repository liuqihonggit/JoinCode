namespace McpToolDispatch.Tests.Handlers;

/// <summary>
/// ToolScoreDebugToolHandlers 单元测试 — 验证评分查询、超图展示、评分重置
/// </summary>
public sealed class ToolScoreDebugToolHandlersTest : IAsyncLifetime
{
    private InMemoryFileSystem _fs = null!;
    private ToolHealthMonitor _monitor = null!;
    private ToolHypergraphScorer _scorer = null!;
    private ToolScoreDebugToolHandlers _handlers = null!;

    private static string GetText(ToolResult result) =>
        result.Content.FirstOrDefault(c => c.Type == ToolContentType.Text)?.Text ?? "";

    public Task InitializeAsync()
    {
        _fs = new InMemoryFileSystem();
        _monitor = new ToolHealthMonitor(_fs, config: new ToolScoreConfig
        {
            SuccessDelta = 1,
            FailDelta = -5,
            CircuitBreakerThreshold = 3
        });
        _scorer = new ToolHypergraphScorer(monitor: _monitor);
        _handlers = new ToolScoreDebugToolHandlers(_monitor, _scorer);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _scorer.Dispose();
        _monitor.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetToolScoreAsync_NoRecords_ReturnsEmptyMessage()
    {
        var result = await _handlers.GetToolScoreAsync(null);
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("暂无工具评分记录");
    }

    [Fact]
    public async Task GetToolScoreAsync_SpecificTool_ShowsScoreDetails()
    {
        await _monitor.RecordSuccessAsync("Read");
        await _monitor.RecordSuccessAsync("Read");
        await _monitor.RecordFailureAsync("Read", "file not found");

        var result = await _handlers.GetToolScoreAsync("Read");
        result.IsError.Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("工具评分: Read");
        text.Should().Contain("独立评分");
        text.Should().Contain("超图评分");
        text.Should().Contain("有效评分");
        text.Should().Contain("成功/失败: 2/1");
    }

    [Fact]
    public async Task GetToolScoreAsync_AllTools_ReturnsTable()
    {
        await _monitor.RecordSuccessAsync("Read");
        await _monitor.RecordSuccessAsync("Write");
        await _monitor.RecordFailureAsync("Write", "permission denied");

        var result = await _handlers.GetToolScoreAsync(null);
        result.IsError.Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("所有工具评分");
        text.Should().Contain("Read");
        text.Should().Contain("Write");
    }

    [Fact]
    public async Task GetHypergraphAsync_ShowsPresets()
    {
        var result = await _handlers.GetHypergraphAsync();
        result.IsError.Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("工具链超图");
        text.Should().Contain("超边总数");
    }

    [Fact]
    public async Task ResetToolScoreAsync_ResetsScoreToZero()
    {
        await _monitor.RecordFailureAsync("failing_tool", "error1");
        await _monitor.RecordFailureAsync("failing_tool", "error2");

        var before = await _monitor.GetRecordAsync("failing_tool");
        before!.FailCount.Should().Be(2);
        before.Score.Should().BeLessThan(0);

        var result = await _handlers.ResetToolScoreAsync("failing_tool");
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("评分已重置");

        var after = await _monitor.GetRecordAsync("failing_tool");
        after.Should().NotBeNull();
        after!.Score.Should().Be(0);
        after.ConsecutiveFailures.Should().Be(0);
        after.IsEnabled.Should().BeTrue();
    }
}
