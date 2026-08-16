namespace Host.Tests.Tui.Rendering;

/// <summary>
/// TerminalResizeMonitor 单元测试 — 验证尺寸钳制、防抖、事件触发。
/// </summary>
public class TerminalResizeMonitorTests
{
    [Fact]
    public void Clamp_BelowMin_ClampedToMin()
    {
        var monitor = new TerminalResizeMonitor();
        var (w, h) = monitor.Clamp(10, 5);
        Assert.Equal(80, w);
        Assert.Equal(24, h);
    }

    [Fact]
    public void Clamp_AboveMax_ClampedToMax()
    {
        var monitor = new TerminalResizeMonitor();
        var (w, h) = monitor.Clamp(1000, 500);
        Assert.Equal(500, w);
        Assert.Equal(200, h);
    }

    [Fact]
    public void Clamp_NormalRange_Unchanged()
    {
        var monitor = new TerminalResizeMonitor();
        var (w, h) = monitor.Clamp(120, 40);
        Assert.Equal(120, w);
        Assert.Equal(40, h);
    }

    [Fact]
    public void IsTooSmall_BelowMin_ReturnsTrue()
    {
        var monitor = new TerminalResizeMonitor();
        Assert.True(monitor.IsTooSmall(70, 20));
        Assert.True(monitor.IsTooSmall(70, 40));
        Assert.True(monitor.IsTooSmall(120, 20));
    }

    [Fact]
    public void IsTooSmall_AtMin_ReturnsFalse()
    {
        var monitor = new TerminalResizeMonitor();
        Assert.False(monitor.IsTooSmall(80, 24));
    }

    [Fact]
    public void GetSafeDefault_Returns120x40()
    {
        var (w, h) = TerminalResizeMonitor.GetSafeDefault();
        Assert.Equal(120, w);
        Assert.Equal(40, h);
    }

    [Fact]
    public async Task CheckAndNotify_SizeChange_TriggersEvent()
    {
        var monitor = new TerminalResizeMonitor(120, 40);
        var changes = new List<(int w, int h)>();
        monitor.SizeChanged += (w, h) => changes.Add((w, h));

        await Task.Delay(250);
        monitor.CheckAndNotify(100, 30);
        Assert.Single(changes);
        Assert.Equal(100, changes[0].w);
        Assert.Equal(30, changes[0].h);
    }

    [Fact]
    public async Task CheckAndNotify_NoChange_DoesNotTrigger()
    {
        var monitor = new TerminalResizeMonitor(120, 40);
        var changes = new List<(int w, int h)>();
        monitor.SizeChanged += (w, h) => changes.Add((w, h));

        await Task.Delay(250);
        monitor.CheckAndNotify(120, 40);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task CheckAndNotify_TooSmall_TriggersTooSmallEvent()
    {
        var monitor = new TerminalResizeMonitor(120, 40);
        var tooSmall = new List<(int w, int h, int minW, int minH)>();
        monitor.SizeTooSmall += (w, h, minW, minH) => tooSmall.Add((w, h, minW, minH));

        await Task.Delay(250);
        monitor.CheckAndNotify(70, 20);
        Assert.Single(tooSmall);
        Assert.Equal(70, tooSmall[0].w);
        Assert.Equal(20, tooSmall[0].h);
    }

    [Fact]
    public async Task CheckAndNotify_Debounce_SkipsRapidChanges()
    {
        var monitor = new TerminalResizeMonitor(120, 40);
        var changes = new List<(int w, int h)>();
        monitor.SizeChanged += (w, h) => changes.Add((w, h));

        await Task.Delay(250);
        monitor.CheckAndNotify(100, 30);
        monitor.CheckAndNotify(110, 35);
        Assert.Single(changes);
    }
}
