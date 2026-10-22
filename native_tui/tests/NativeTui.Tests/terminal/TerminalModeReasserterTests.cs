namespace NativeTui.Tests.Terminal;

/// <summary>
/// stdin 长静默 gap 检测测试 — 基准 App.tsx:30-35 STDIN_RESUME_GAP_MS=5000 + :359-368：
/// 捕捉 tmux detach→attach / ssh 重连 / 休眠唤醒 — 终端侧 DEC 模式已被重置时自愈重发。
/// </summary>
public sealed class TerminalModeReasserterTests
{
    /// <summary>静默超 gap → 返回 true（触发重断言）并重置计时。</summary>
    [Fact]
    public void IdleBeyondGap_ReturnsTrue_AndResetsTimer()
    {
        var reasserter = new TerminalModeReasserter(gapMs: 5000);
        reasserter.MarkInputReceived(nowTick: 1000);

        Assert.True(reasserter.ShouldReassert(nowTick: 1000 + 5001));

        // 重断言后立即再查 — 计时已重置不重复触发
        Assert.False(reasserter.ShouldReassert(nowTick: 1000 + 5002));
    }

    /// <summary>静默未超 gap — 不触发（正常打字间隔）。</summary>
    [Fact]
    public void IdleWithinGap_ReturnsFalse()
    {
        var reasserter = new TerminalModeReasserter(gapMs: 5000);
        reasserter.MarkInputReceived(nowTick: 1000);

        Assert.False(reasserter.ShouldReassert(nowTick: 1000 + 4999));
    }

    /// <summary>从未收到输入（未武装）— 不触发（启动期无输入不误发）。</summary>
    [Fact]
    public void NotArmed_ReturnsFalse()
    {
        var reasserter = new TerminalModeReasserter(gapMs: 5000);

        Assert.False(reasserter.ShouldReassert(nowTick: 999_999));
    }

    /// <summary>打点刷新静默计时（连续输入永不触发）。</summary>
    [Fact]
    public void ContinuousInput_NeverTriggers()
    {
        var reasserter = new TerminalModeReasserter(gapMs: 5000);
        reasserter.MarkInputReceived(nowTick: 1000);

        for (var now = 2000; now <= 10_000; now += 1000)
        {
            Assert.False(reasserter.ShouldReassert(now));
            reasserter.MarkInputReceived(now);
        }
    }

    /// <summary>持续静默 — 每 gap 周期触发一次（周期性重断言直到输入恢复）。</summary>
    [Fact]
    public void PersistentIdle_TriggersEveryGap()
    {
        var reasserter = new TerminalModeReasserter(gapMs: 5000);
        reasserter.MarkInputReceived(nowTick: 0);

        Assert.False(reasserter.ShouldReassert(4000));
        Assert.True(reasserter.ShouldReassert(5001));
        Assert.False(reasserter.ShouldReassert(9000));
        Assert.True(reasserter.ShouldReassert(10_002));
    }
}
