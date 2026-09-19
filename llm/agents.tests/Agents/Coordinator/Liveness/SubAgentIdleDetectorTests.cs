namespace Sync.Tests.Agents.Coordinator.Liveness;

/// <summary>
/// SubAgentIdleDetector 单元测试 — 验证状态机转换 + 时间窗口二次确认（ADR 0106 L2）
/// </summary>
public sealed class SubAgentIdleDetectorTests {
    private static DateTimeOffset Time(int seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);

    [Fact]
    public void Record_ActivityWhenMonitoring_StaysMonitoring() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // lastActivity 刚刚刷新，未超阈值
        var result = detector.Record(now, hasGrandchildren: false);

        result.State.Should().Be(SubAgentLivenessState.Monitoring);
        result.IsStalled.Should().BeFalse();
    }

    [Fact]
    public void Record_IdleWhenMonitoring_TransitionsToSuspected() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // lastActivity 在 40s 前，超过 30s 阈值
        var result = detector.Record(Time(60), hasGrandchildren: false);

        result.State.Should().Be(SubAgentLivenessState.Suspected);
        result.Event.Should().Be(SubAgentLivenessEvent.Idle);
        result.IsStalled.Should().BeFalse();
    }

    [Fact]
    public void Record_IdleAgainInWindow_TransitionsToConfirmed() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // 第一次 Idle → Suspected
        detector.Record(Time(60), hasGrandchildren: false);
        detector.State.Should().Be(SubAgentLivenessState.Suspected);

        // 3s 后（在 5s 窗口内）再次 Idle → Confirmed
        now = Time(103);
        var result = detector.Record(Time(60), hasGrandchildren: false);

        result.State.Should().Be(SubAgentLivenessState.Confirmed);
        result.IsStalled.Should().BeTrue();
    }

    [Fact]
    public void Record_ActiveInWindow_TransitionsBackToMonitoring() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // 第一次 Idle → Suspected
        detector.Record(Time(60), hasGrandchildren: false);

        // 3s 后（在窗口内）恢复活动 → Monitoring
        now = Time(103);
        var result = detector.Record(now, hasGrandchildren: false);

        result.State.Should().Be(SubAgentLivenessState.Monitoring);
    }

    [Fact]
    public void Record_IdleAfterWindowExceeds_TransitionsToConfirmed() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // 第一次 Idle → Suspected
        detector.Record(Time(60), hasGrandchildren: false);

        // 10s 后（超过 5s 窗口）仍 Idle → Confirm → Confirmed
        now = Time(110);
        var result = detector.Record(Time(60), hasGrandchildren: false);

        result.State.Should().Be(SubAgentLivenessState.Confirmed);
        result.IsStalled.Should().BeTrue();
    }

    [Fact]
    public void Record_ActiveAfterWindow_TransitionsToMonitoring() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // 第一次 Idle → Suspected
        detector.Record(Time(60), hasGrandchildren: false);

        // 10s 后（超过窗口）恢复活动 → Timeout → Monitoring
        now = Time(110);
        var result = detector.Record(now, hasGrandchildren: false);

        result.State.Should().Be(SubAgentLivenessState.Monitoring);
    }

    [Fact]
    public void Record_HasGrandchildren_NotConsideredIdle() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // 超过阈值但有孙代理 → 不算 Idle
        var result = detector.Record(Time(60), hasGrandchildren: true);

        result.State.Should().Be(SubAgentLivenessState.Monitoring);
        result.IsStalled.Should().BeFalse();
    }

    [Fact]
    public void MarkRecovered_FromConfirmed_TransitionsToMonitoring() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // 走到 Confirmed
        detector.Record(Time(60), hasGrandchildren: false);
        now = Time(103);
        detector.Record(Time(60), hasGrandchildren: false);
        detector.State.Should().Be(SubAgentLivenessState.Confirmed);

        // 标记恢复
        detector.MarkRecovered();
        detector.State.Should().Be(SubAgentLivenessState.Monitoring);
    }

    [Fact]
    public void Reset_AlwaysGoesToMonitoring() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // 走到 Confirmed
        detector.Record(Time(60), hasGrandchildren: false);
        now = Time(103);
        detector.Record(Time(60), hasGrandchildren: false);

        // 重置
        detector.Reset();
        detector.State.Should().Be(SubAgentLivenessState.Monitoring);
    }

    [Fact]
    public void Record_ConfirmedAndStillIdle_StaysConfirmed() {
        var now = Time(100);
        var detector = new SubAgentIdleDetector(
            idleThreshold: TimeSpan.FromSeconds(30),
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => now);

        // 走到 Confirmed
        detector.Record(Time(60), hasGrandchildren: false);
        now = Time(103);
        detector.Record(Time(60), hasGrandchildren: false);
        detector.State.Should().Be(SubAgentLivenessState.Confirmed);

        // 继续Cidle → 保持 Confirmed
        now = Time(200);
        var result = detector.Record(Time(60), hasGrandchildren: false);
        result.State.Should().Be(SubAgentLivenessState.Confirmed);
    }
}