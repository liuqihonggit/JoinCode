namespace NativeTui.Tests.Repl;

/// <summary>
/// WheelSpeedTracker 滚轮加速度 ramp + 方向翻转弹跳测试（TASK066 C5 P3 + 板块 E 收尾 —
/// 基准 ScrollKeybindingHandler.tsx computeWheelStep native 路径）：
/// ramp 段（:255-265）：40ms 判定窗内连续事件乘数 +0.3 递增（floor 序列 1,1,1,1,2,2,2,3,...
/// 第 5 事件起 2 行 — 乘数为浮点累积非整数计数）、首事件/超窗重置回 base、
/// cap = max(6, base×2) 钳制（:262）、gap=40 恰在窗内（:259 gap &gt; 窗才重置）。
/// 弹跳段（:177-217）：方向翻转事件挂起返回 0（不滚动 — :208-217 defer）；下一事件
/// 200ms 窗内翻回原方向 = 编码器弹跳吞掉继续原方向 ramp（:201-205 — dir/mult 保持）；
/// 持续新方向 = 真翻转提交 mult 重置回 base（:196-199）；超窗翻回同样按真翻转重置（:193）。
/// base 旋钮解析 clamp (0,20] 默认 1（readScrollSpeedBase :305-310 — parseFloat 前缀语义）。
/// 时钟经构造注入（可控时间戳推进 — 对齐 SelectionState/EscClock 注入先例）。
/// 浮点断言避开整数边界单点（如乘数 4.0±ε 的 floor 翻转）— 只断言安全子序列。
/// </summary>
public sealed class WheelSpeedTrackerTests
{
    /// <summary>构造带可控时钟的跟踪器（闭包捕获可变局部 — advance 推进即推进注入时钟；
    /// 起始 1000ms 远离时间原点，判定只依赖事件间推进量）。</summary>
    private static (WheelSpeedTracker Tracker, Action<long> Advance) ManualClock(double baseLines = 1)
    {
        long now = 1000;
        return (new WheelSpeedTracker(() => now, baseLines), ms => now += ms);
    }

    /// <summary>单发事件步长 base — 首事件重置回 base（1 行；基准 :259-260 首事件/超窗路径，
    /// 离散单事件等价 base 行 — C5 原固定 1 行行为）。</summary>
    [Fact]
    public void SingleEvent_ReturnsBaseStep()
    {
        var (tracker, advance) = ManualClock();

        advance(500);
        Assert.Equal(1, tracker.OnWheelEvent(true));
    }

    /// <summary>40ms 窗内连发递增 — 乘数 1→1.3→1.6→1.9→2.2→2.5→2.8→3.1→3.4→3.7，
    /// floor 序列 1,1,1,1,2,2,2,3,3,3（基准 :262-263 mult+0.3 钳 cap；第 5 事件起 2 行；
    /// 浮点断言止于 3.7 — 避开 4.0±ε 整数边界的 floor 翻转单点）。</summary>
    [Fact]
    public void BurstsWithinWindow_RampStepwise()
    {
        var (tracker, advance) = ManualClock();
        var steps = new int[10];
        for (var i = 0; i < steps.Length; i++)
        {
            advance(10);
            steps[i] = tracker.OnWheelEvent(true);
        }

        Assert.Equal(new[] { 1, 1, 1, 1, 2, 2, 2, 3, 3, 3 }, steps);
    }

    /// <summary>超窗重置回 base — 5 事件 ramp 至 2 行后间隔 60ms（&gt; 40ms 窗）单发回 1 行
    ///（基准 :259-260 gap &gt; WHEEL_ACCEL_WINDOW_MS → mult = base）。</summary>
    [Fact]
    public void GapBeyondWindow_ResetsToBase()
    {
        var (tracker, advance) = ManualClock();
        tracker.OnWheelEvent(true);
        for (var i = 0; i < 4; i++)
        {
            advance(10);
            tracker.OnWheelEvent(true);
        }

        advance(10);
        Assert.Equal(2, tracker.OnWheelEvent(true));

        advance(60);
        Assert.Equal(1, tracker.OnWheelEvent(true));
    }

    /// <summary>cap 6 钳制 — 持续窗内连发：乘数 1+0.3n 第 18 事件达 6.1 钳 6
    ///（cap = max(6, base×2) = 6 — 基准 :262），第 21 事件起恒 6 不再增长。</summary>
    [Fact]
    public void SustainedBurst_ClampsAtCap()
    {
        var (tracker, advance) = ManualClock();
        var steps = new List<int>();
        for (var i = 0; i < 40; i++)
        {
            advance(10);
            steps.Add(tracker.OnWheelEvent(true));
        }

        Assert.Equal(1, steps[0]);
        Assert.Equal(6, steps[17]);
        Assert.All(steps.Skip(20), step => Assert.Equal(6, step));
    }

    /// <summary>gap 恰为 40ms 仍在窗内 — 基准 :259 gap &gt; 40 才重置（40 属窗内边界）：
    /// 连续 40ms 间隔事件持续 ramp（乘数 1→1.3→1.6→1.9→2.2，第 5 事件 2 行 — 误判重置则恒 1）；
    /// gap=41 超窗重置回 base。</summary>
    [Fact]
    public void GapExactlyAtWindow_StillRamps()
    {
        var (tracker, advance) = ManualClock();
        tracker.OnWheelEvent(true);

        advance(40);
        Assert.Equal(1, tracker.OnWheelEvent(true));
        advance(40);
        Assert.Equal(1, tracker.OnWheelEvent(true));
        advance(40);
        Assert.Equal(1, tracker.OnWheelEvent(true));
        advance(40);
        Assert.Equal(2, tracker.OnWheelEvent(true));

        advance(41);
        Assert.Equal(1, tracker.OnWheelEvent(true));
    }

    /// <summary>base &gt; 默认时 cap = max(6, base×2) — base=4 → cap 8（基准 :262 高 base
    /// 旋钮时上限随之上抬）：首事件 4 行、连发 ramp 至 8 钳制。</summary>
    [Fact]
    public void HighBase_CapDoublesAboveSix()
    {
        var (tracker, advance) = ManualClock(baseLines: 4);
        Assert.Equal(4, tracker.OnWheelEvent(true));

        var steps = new List<int>();
        for (var i = 0; i < 30; i++)
        {
            advance(10);
            steps.Add(tracker.OnWheelEvent(true));
        }

        Assert.Equal(8, steps[^1]);
        Assert.All(steps.Skip(20), step => Assert.Equal(8, step));
    }

    /// <summary>时钟注入可控推进 — 注入时钟是唯一时间真相源：不推进（间隔 0 恒窗内）连发
    /// ramp 出加速序列，推进 100ms（超窗）单发即重置回 base，同一跟踪器两模式结果分叉。</summary>
    [Fact]
    public void InjectedClockAdvancement_ControlsRampState()
    {
        long now = 1000;
        var tracker = new WheelSpeedTracker(() => now);
        var ramped = new List<int>();
        for (var i = 0; i < 10; i++)
        {
            ramped.Add(tracker.OnWheelEvent(true));
        }

        now += 100;
        Assert.Equal(1, tracker.OnWheelEvent(true));
        Assert.Equal(new[] { 1, 1, 1, 1, 2, 2, 2, 3, 3, 3 }, ramped);
    }

    // ==== 方向翻转弹跳（基准 computeWheelStep :177-217 — 板块 E 收尾）====

    /// <summary>方向翻转事件挂起返回 0 — 翻转不立即滚动（基准 :208-217 defer：本事件
    /// 0 行，调用方 ScrollBy(0) no-op），下一同新方向事件才提交翻转。</summary>
    [Fact]
    public void DirectionFlip_DefersZeroStep()
    {
        var (tracker, advance) = ManualClock();
        tracker.OnWheelEvent(true);

        advance(10);
        Assert.Equal(0, tracker.OnWheelEvent(false));
    }

    /// <summary>真翻转提交重置回 base — 下滚连发 ramp 至 2 行后翻转上滚：挂起事件 0，
    /// 第二个同新方向事件提交翻转（mult 重置回 base — 基准 :196-199）返回 base 行 1
    ///（非翻转前 2.2+0.3=2 行 — UpDown 共用乘数状态泄漏即此反例）。</summary>
    [Fact]
    public void DirectionFlipCommits_MultResetsToBase()
    {
        var (tracker, advance) = ManualClock();
        for (var i = 0; i < 5; i++)
        {
            advance(10);
            tracker.OnWheelEvent(true);
        }

        advance(10);
        Assert.Equal(0, tracker.OnWheelEvent(false));

        advance(10);
        Assert.Equal(1, tracker.OnWheelEvent(false));
    }

    /// <summary>翻转提交后同向再连发重新累积 — 上滚翻转提交后窗内连发从 base 重新 ramp
    ///（floor 序列 1,1,1,1,2 — 与全新跟踪器同向连发序列一致）。</summary>
    [Fact]
    public void AfterFlipCommit_SameNewDirectionBurstRampsFromBaseAgain()
    {
        var (tracker, advance) = ManualClock();
        for (var i = 0; i < 5; i++)
        {
            advance(10);
            tracker.OnWheelEvent(true);
        }

        advance(10);
        tracker.OnWheelEvent(false);
        advance(10);
        tracker.OnWheelEvent(false);

        var steps = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            advance(10);
            steps.Add(tracker.OnWheelEvent(false));
        }

        Assert.Equal(new[] { 1, 1, 1, 2, 2 }, steps);
    }

    /// <summary>编码器弹跳吞掉 — 翻转事件挂起后窗内（≤200ms）翻回原方向 = 弹跳确认
    ///（基准 :201-205：dir/mult 保持翻转前状态），本事件继续原方向 ramp 累积
    ///（下滚 ramp 至 2.2 → 挂起 0 → 弹跳 2.5 → 2 行；误判重置则回 1 行）。</summary>
    [Fact]
    public void EncoderBounce_FlipBackWithinWindow_SwallowsAndKeepsOriginalRamp()
    {
        var (tracker, advance) = ManualClock();
        for (var i = 0; i < 5; i++)
        {
            advance(10);
            tracker.OnWheelEvent(true);
        }

        advance(10);
        Assert.Equal(0, tracker.OnWheelEvent(false));

        advance(10);
        Assert.Equal(2, tracker.OnWheelEvent(true));
    }

    /// <summary>弹跳判定窗边界 — 翻转挂起后超窗（&gt; 200ms）翻回原方向不按弹跳处理：
    /// 基准 :193 `now - state.time &gt; WHEEL_BOUNCE_GAP_MAX_MS` → 真翻转提交重置回 base
    ///（长时间隔说明非编码器噪声，两连真实改向）。</summary>
    [Fact]
    public void BounceGapExceeded_FlipBackTreatedAsRealReversal()
    {
        var (tracker, advance) = ManualClock();
        for (var i = 0; i < 5; i++)
        {
            advance(10);
            tracker.OnWheelEvent(true);
        }

        advance(10);
        Assert.Equal(0, tracker.OnWheelEvent(false));

        advance(201);
        Assert.Equal(1, tracker.OnWheelEvent(true));
    }

    /// <summary>弹跳确认后继续同向连发 — 弹跳事件已计入 cadence（基准 :201-205 注释
    /// bounce IS a physical click），乘数延续原方向累积（2.2 → 弹跳 2.5 → 2.8→3.1→3.4
    /// → 2,3,3 行）。</summary>
    [Fact]
    public void AfterBounceConfirm_OriginalDirectionBurstContinuesRamp()
    {
        var (tracker, advance) = ManualClock();
        for (var i = 0; i < 5; i++)
        {
            advance(10);
            tracker.OnWheelEvent(true);
        }

        advance(10);
        tracker.OnWheelEvent(false);
        advance(10);
        tracker.OnWheelEvent(true);

        var steps = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            advance(10);
            steps.Add(tracker.OnWheelEvent(true));
        }

        Assert.Equal(new[] { 2, 3, 3 }, steps);
    }

    /// <summary>首事件不算翻转 — 无先前方向时首事件（任一方向）直接走 ramp 重置回 base
    ///（基准 :208 state.dir !== 0 首事件守卫），返回 base 行非 0。</summary>
    [Fact]
    public void FirstEvent_AnyDirection_NoDefer()
    {
        var (tracker, advance) = ManualClock();

        advance(500);
        Assert.Equal(1, tracker.OnWheelEvent(false));
    }

    /// <summary>CLAUDE_CODE_SCROLL_SPEED 旋钮解析 — clamp (0,20] 默认 1（基准 readScrollSpeedBase
    /// :305-310）：缺失/空/纯空白/非数字/≤0 → 1；parseFloat 前缀语义（"3x"→3、"+2"→2、
    /// 前导空白跳过）；上界 20 含（超值钳 20）。</summary>
    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("x", 1)]
    [InlineData("-", 1)]
    [InlineData(".", 1)]
    [InlineData("-3", 1)]
    [InlineData("0", 1)]
    [InlineData("0.5", 0.5)]
    [InlineData("3", 3)]
    [InlineData("1.5", 1.5)]
    [InlineData("20", 20)]
    [InlineData("20.5", 20)]
    [InlineData("25", 20)]
    [InlineData("3x", 3)]
    [InlineData("+2", 2)]
    [InlineData("  7 ", 7)]
    public void ParseScrollSpeedBase_ClampsToZeroTwentyDefaultOne(string? raw, double expected)
    {
        Assert.Equal(expected, WheelSpeedTracker.ParseScrollSpeedBase(raw));
    }
}
