namespace Abs.Tests.Tools;

/// <summary>
/// ToolHealthMonitorExtensions 单元测试 — 验证 ShouldAutoFixAsync / GetErrorCountAsync 扩展方法
/// </summary>
public sealed class ToolHealthMonitorExtensionsTest
{
    // === ShouldAutoFixAsync ===

    [Fact]
    public async Task ShouldAutoFixAsync_RecordNull_ReturnsFalse()
    {
        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
               .ReturnsAsync((ToolHealthRecord?)null);

        var result = await monitor.Object.ShouldAutoFixAsync("tool1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAutoFixAsync_ConsecutiveFailuresBelowThreshold_ReturnsFalse()
    {
        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 2 });

        var result = await monitor.Object.ShouldAutoFixAsync("tool1", threshold: 3);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAutoFixAsync_ConsecutiveFailuresAtThreshold_ReturnsTrue()
    {
        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 3 });

        var result = await monitor.Object.ShouldAutoFixAsync("tool1", threshold: 3);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldAutoFixAsync_ConsecutiveFailuresAboveThreshold_ReturnsTrue()
    {
        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 5 });

        var result = await monitor.Object.ShouldAutoFixAsync("tool1", threshold: 3);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldAutoFixAsync_DefaultThreshold_Is3()
    {
        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 3 });

        var result = await monitor.Object.ShouldAutoFixAsync("tool1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldAutoFixAsync_DefaultThreshold_Below3_ReturnsFalse()
    {
        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 2 });

        var result = await monitor.Object.ShouldAutoFixAsync("tool1");

        result.Should().BeFalse();
    }

    // === GetErrorCountAsync ===

    [Fact]
    public async Task GetErrorCountAsync_RecordNull_ReturnsZero()
    {
        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
               .ReturnsAsync((ToolHealthRecord?)null);

        var result = await monitor.Object.GetErrorCountAsync("tool1");

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetErrorCountAsync_RecordExists_ReturnsConsecutiveFailures()
    {
        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 7 });

        var result = await monitor.Object.GetErrorCountAsync("tool1");

        result.Should().Be(7);
    }

    [Fact]
    public async Task GetErrorCountAsync_ZeroFailures_ReturnsZero()
    {
        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 0 });

        var result = await monitor.Object.GetErrorCountAsync("tool1");

        result.Should().Be(0);
    }
}
