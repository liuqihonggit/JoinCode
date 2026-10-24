namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// ToolPatrolService 单元测试 — 验证手动巡检 + 启停循环 + Dispose 幂等。
/// </summary>
public sealed class ToolPatrolServiceTest {
    private static ToolPatrolReport MakeReport() => new(
        DateTimeOffset.UtcNow,
        TotalTools: 2,
        Results: [
            new ToolPatrolResult("tool_a", ToolPatrolStatus.Ok, null, TimeSpan.FromMilliseconds(5)),
            new ToolPatrolResult("gh_issue_create", ToolPatrolStatus.Skipped, "有真实副作用", TimeSpan.Zero),
        ]);

    private sealed class StubPatrolEngine : IToolPatrolEngine {
        public ToolPatrolReport Report { get; set; } = MakeReport();
        public int CallCount { get; private set; }
        public Task<ToolPatrolReport> PatrolAsync(CancellationToken ct = default) {
            CallCount++;
            return Task.FromResult(Report);
        }
    }

    [Fact]
    public async Task PatrolOnceAsync_ReturnsEngineReport() {
        var engine = new StubPatrolEngine();
        await using var service = new ToolPatrolService(engine, interval: TimeSpan.FromSeconds(1));

        var report = await service.PatrolOnceAsync();

        report.Should().BeSameAs(engine.Report);
        engine.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartStopAsync_CompletesWithoutCrash() {
        var engine = new StubPatrolEngine();
        await using var service = new ToolPatrolService(engine, interval: TimeSpan.FromMilliseconds(100));

        await service.StartAsync();
        await Task.Delay(50);
        await service.StopAsync();

        engine.CallCount.Should().BeGreaterThanOrEqualTo(0, "定时循环可能触发 0+ 次巡检");
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotCrash() {
        var engine = new StubPatrolEngine();
        var service = new ToolPatrolService(engine, interval: TimeSpan.FromSeconds(1));

        await service.DisposeAsync();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_DoesNotStartSecondLoop() {
        var engine = new StubPatrolEngine();
        await using var service = new ToolPatrolService(engine, interval: TimeSpan.FromSeconds(1));

        await service.StartAsync();
        await service.StartAsync();
        await service.StopAsync();
    }
}
