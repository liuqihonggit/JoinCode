// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Utils;

/// <summary>
/// WatermarkMonitor 确定性单元测试 — 验证水位防抖逻辑,不依赖时序/不启动 Actor。
/// <para>覆盖:首次触发、防抖不重复、回落重置、Critical→High 不重复、Reset、事件参数正确。</para>
/// </summary>
public class WatermarkMonitorTest {
    private static WatermarkMonitor NewMonitor(int high = 50, int critical = 80, int capacity = 100)
        => new(new ActorBackpressure(Capacity: capacity, HighWatermark: high, CriticalWatermark: critical));

    /// <summary>首次到达高水位线 → 触发 High 事件</summary>
    [Fact]
    public void FirstHighWatermark_TriggersHighEvent() {
        var monitor = NewMonitor();
        var triggered = monitor.Check(50, "TestActor", out var args);

        triggered.Should().BeTrue();
        args.Should().NotBeNull();
        args!.Level.Should().Be(WatermarkLevel.High);
        args.CurrentCount.Should().Be(50);
        args.Capacity.Should().Be(100);
        args.ActorId.Should().Be("TestActor");
    }

    /// <summary>High 已触发后再次 Check High → 不重复触发(防抖核心)</summary>
    [Fact]
    public void HighWatermark_AlreadyFired_DoesNotRetrigger() {
        var monitor = NewMonitor();
        monitor.Check(50, "A", out _);

        var second = monitor.Check(60, "A", out var args2);

        second.Should().BeFalse("High 已触发,维持在高位不重复告警(防抖)");
        args2.Should().BeNull();
    }

    /// <summary>首次到达危险水位线 → 触发 Critical 事件</summary>
    [Fact]
    public void FirstCriticalWatermark_TriggersCriticalEvent() {
        var monitor = NewMonitor();
        var triggered = monitor.Check(80, "A", out var args);

        triggered.Should().BeTrue();
        args.Should().NotBeNull();
        args!.Level.Should().Be(WatermarkLevel.Critical);
        args.CurrentCount.Should().Be(80);
    }

    /// <summary>Critical 已触发后再次 Check Critical → 不重复触发</summary>
    [Fact]
    public void CriticalWatermark_AlreadyFired_DoesNotRetrigger() {
        var monitor = NewMonitor();
        monitor.Check(80, "A", out _);

        var second = monitor.Check(90, "A", out var args2);

        second.Should().BeFalse("Critical 已触发,不重复告警(防抖)");
        args2.Should().BeNull();
    }

    /// <summary>从 Critical 直接到 High(不经过 Normal) → 不重复触发 High</summary>
    [Fact]
    public void CriticalToHigh_WithoutNormal_DoesNotRetriggerHigh() {
        var monitor = NewMonitor();
        monitor.Check(80, "A", out _); // Critical 触发,同时标记 High

        var highAfterCritical = monitor.Check(60, "A", out var args);

        highAfterCritical.Should().BeFalse("从 Critical 降到 High 未经过 Normal,High 已标记不重复触发");
        args.Should().BeNull();
    }

    /// <summary>回落到 Normal → 重置防抖状态 → 再次 High 可触发</summary>
    [Fact]
    public void NormalWatermark_ResetsState_AllowsRetrigger() {
        var monitor = NewMonitor();
        monitor.Check(50, "A", out _);  // High 触发
        monitor.Check(10, "A", out var normalArgs); // Normal 重置

        normalArgs.Should().BeNull("Normal 不触发事件");

        var retriggered = monitor.Check(50, "A", out var reArgs);

        retriggered.Should().BeTrue("回落后重置,再次 High 重新触发");
        reArgs.Should().NotBeNull();
        reArgs!.Level.Should().Be(WatermarkLevel.High);
    }

    /// <summary>回落 Normal 后再次 Critical 可触发</summary>
    [Fact]
    public void NormalWatermark_ResetsState_AllowsCriticalRetrigger() {
        var monitor = NewMonitor();
        monitor.Check(80, "A", out _);  // Critical 触发
        monitor.Check(10, "A", out _);  // Normal 重置

        var retriggered = monitor.Check(80, "A", out var reArgs);

        retriggered.Should().BeTrue("回落后重置,再次 Critical 重新触发");
        reArgs!.Level.Should().Be(WatermarkLevel.Critical);
    }

    /// <summary>Reset 后可重新触发(测试与恢复场景)</summary>
    [Fact]
    public void Reset_AllowsRetrigger() {
        var monitor = NewMonitor();
        monitor.Check(80, "A", out _);  // Critical 触发
        monitor.Reset();

        var retriggered = monitor.Check(80, "A", out var args);

        retriggered.Should().BeTrue("Reset 清除防抖状态,重新触发");
        args!.Level.Should().Be(WatermarkLevel.Critical);
    }

    /// <summary>低于高水位线 → Normal,不触发</summary>
    [Fact]
    public void BelowHighWatermark_NoTrigger() {
        var monitor = NewMonitor();
        var triggered = monitor.Check(49, "A", out var args);

        triggered.Should().BeFalse();
        args.Should().BeNull();
    }

    /// <summary>事件参数完整正确(ActorId/CurrentCount/Capacity/Level)</summary>
    [Fact]
    public void EventArgs_ContainCorrectValues() {
        var monitor = NewMonitor(high: 5, critical: 10, capacity: 30);
        monitor.Check(5, "MyActor", out var highArgs);
        monitor.Check(0, "MyActor", out _); // 重置
        monitor.Check(10, "MyActor", out var critArgs);

        highArgs.Should().NotBeNull();
        highArgs!.ActorId.Should().Be("MyActor");
        highArgs.CurrentCount.Should().Be(5);
        highArgs.Capacity.Should().Be(30);
        highArgs.Level.Should().Be(WatermarkLevel.High);

        critArgs.Should().NotBeNull();
        critArgs!.ActorId.Should().Be("MyActor");
        critArgs.CurrentCount.Should().Be(10);
        critArgs.Capacity.Should().Be(30);
        critArgs.Level.Should().Be(WatermarkLevel.Critical);
    }

    /// <summary>连续多次 Normal 调用保持重置状态(幂等)</summary>
    [Fact]
    public void MultipleNormalCalls_IdempotentReset() {
        var monitor = NewMonitor();
        monitor.Check(50, "A", out _); // High

        monitor.Check(0, "A", out _);
        monitor.Check(0, "A", out _);
        monitor.Check(0, "A", out _);

        var triggered = monitor.Check(50, "A", out var args);
        triggered.Should().BeTrue("多次 Normal 重置后仍可触发");
        args!.Level.Should().Be(WatermarkLevel.High);
    }
}
