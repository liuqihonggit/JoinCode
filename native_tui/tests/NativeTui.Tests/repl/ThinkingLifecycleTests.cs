namespace NativeTui.Tests.Repl;

/// <summary>
/// 思考状态生命周期测试 — 对齐基准 Spinner.tsx thinkingStatus 状态机：
/// 'thinking' 最短展示 2s（不足补足）→ 'thought for Ns'（真实时长）展示 2s → null。
/// 纯状态机 + 墙钟注入（基准 setTimeout 语义离散化为帧循环判定）。
/// </summary>
public sealed class ThinkingLifecycleTests
{
    [Fact]
    public void Tick_ThinkingStarts_ReturnsActive()
    {
        var lifecycle = new ThinkingLifecycle();
        Assert.IsType<ThinkingState.Active>(lifecycle.Tick(thinkingNow: true, wallNowMs: 1000));
    }

    [Fact]
    public void Tick_ThinkingPersists_AcrossMultipleTicks()
    {
        var lifecycle = new ThinkingLifecycle();
        for (var t = 0L; t <= 3000; t += 500)
        {
            Assert.IsType<ThinkingState.Active>(lifecycle.Tick(thinkingNow: true, wallNowMs: t));
        }
    }

    [Fact]
    public void Tick_EffortSuffix_CarriedOnActive()
    {
        var lifecycle = new ThinkingLifecycle();
        var active = Assert.IsType<ThinkingState.Active>(lifecycle.Tick(true, 0, " hard"));
        Assert.Equal(" hard", active.EffortSuffix);
    }

    [Fact]
    public void Tick_NeverThought_ReturnsNull()
    {
        var lifecycle = new ThinkingLifecycle();
        Assert.Equal(ThinkingState.None, lifecycle.Tick(thinkingNow: false, wallNowMs: 0));
    }

    [Fact]
    public void Tick_StopBefore2s_ExtendsActiveThenShowsDuration()
    {
        var lifecycle = new ThinkingLifecycle();
        lifecycle.Tick(true, 0);
        lifecycle.Tick(true, 500);

        // 停止 @500ms：thinking 补足到 2000ms 总展示
        Assert.IsType<ThinkingState.Active>(lifecycle.Tick(false, 500));
        Assert.IsType<ThinkingState.Active>(lifecycle.Tick(false, 1999));
        var thought = Assert.IsType<ThinkingState.ThoughtFor>(lifecycle.Tick(false, 2000));
        Assert.Equal(500, thought.DurationMs);
    }

    [Fact]
    public void Tick_StopAfter2s_ShowsDurationImmediately()
    {
        var lifecycle = new ThinkingLifecycle();
        lifecycle.Tick(true, 0);
        lifecycle.Tick(true, 3000);

        var thought = Assert.IsType<ThinkingState.ThoughtFor>(lifecycle.Tick(false, 3000));
        Assert.Equal(3000, thought.DurationMs);
    }

    [Fact]
    public void Tick_ShortThinking_DurationClearsAfter2sWindow()
    {
        var lifecycle = new ThinkingLifecycle();
        lifecycle.Tick(true, 0);
        lifecycle.Tick(false, 500);

        // duration 显示窗口 [2000, 4000)
        Assert.IsType<ThinkingState.ThoughtFor>(lifecycle.Tick(false, 3999));
        Assert.Equal(ThinkingState.None, lifecycle.Tick(false, 4000));
    }

    [Fact]
    public void Tick_LongThinking_DurationWindowIs2sFromStop()
    {
        var lifecycle = new ThinkingLifecycle();
        lifecycle.Tick(true, 0);
        lifecycle.Tick(false, 5000);

        Assert.IsType<ThinkingState.ThoughtFor>(lifecycle.Tick(false, 6999));
        Assert.Equal(ThinkingState.None, lifecycle.Tick(false, 7000));
    }

    [Fact]
    public void Tick_RestartAfterClear_StartsNewCycle()
    {
        var lifecycle = new ThinkingLifecycle();
        lifecycle.Tick(true, 0);
        lifecycle.Tick(false, 500);
        lifecycle.Tick(false, 5000);
        Assert.Equal(ThinkingState.None, lifecycle.Tick(false, 6000));

        // 重新思考：新周期
        Assert.IsType<ThinkingState.Active>(lifecycle.Tick(true, 7000));
    }

    [Fact]
    public void Tick_ResumeWhileExtending_RestartsThinking()
    {
        var lifecycle = new ThinkingLifecycle();
        lifecycle.Tick(true, 0);
        lifecycle.Tick(false, 500);

        // 延长展示期内重新进入 thinking：重新计时（start=1500）
        Assert.IsType<ThinkingState.Active>(lifecycle.Tick(true, 1500));
        Assert.IsType<ThinkingState.Active>(lifecycle.Tick(true, 2500));

        // 停止 @2500：duration=1000，thinking 补足到 3500
        Assert.IsType<ThinkingState.Active>(lifecycle.Tick(false, 2500));
        Assert.IsType<ThinkingState.Active>(lifecycle.Tick(false, 3499));
        var thought = Assert.IsType<ThinkingState.ThoughtFor>(lifecycle.Tick(false, 3500));
        Assert.Equal(1000, thought.DurationMs);
    }
}
