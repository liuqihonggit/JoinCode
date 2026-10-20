namespace NativeTui.Tests.Animation;

/// <summary>
/// Spinner 动画核心 L3 帧级测试 — 像素级对齐基准 components/Spinner：
/// SpinnerGlyph/utils（12 帧往返弹跳、120ms/帧、停滞红 RGB 线性插值）、
/// useStalledAnimation（3s 阈值 + 2s 渐变 + 每 50ms 步进 10% 平滑）、
/// useShimmerAnimation（requesting 50ms/其余 200ms、cycleLength=宽+20、方向互反）、
/// SpinnerAnimationRow（tool-use flash 正弦、token 计数平滑）。
/// 全部期望值由基准源码逐行推演（JS Math.floor/Math.round 语义）。
/// </summary>
public sealed class SpinnerMathTests
{
    [Fact]
    public void Frames_AreTwelveFrameBounce()
    {
        // 非 darwin 默认字符集：· ✢ * ✶ ✻ ✽（utils.ts getDefaultCharacters）
        // SPINNER_FRAMES = [...默认, ...反转] — 往返弹跳，首尾 · 中间 ✽ 双生
        Assert.Equal(
        [
            "·", "✢", "*", "✶", "✻", "✽",
            "✽", "✻", "✶", "*", "✢", "·",
        ], SpinnerMath.SpinnerFrames);
    }

    [Fact]
    public void Frame_AdvancesEvery120Ms()
    {
        Assert.Equal(0, SpinnerMath.FrameOf(0));
        Assert.Equal(0, SpinnerMath.FrameOf(119));
        Assert.Equal(1, SpinnerMath.FrameOf(120));
        Assert.Equal(12, SpinnerMath.FrameOf(1440));
    }

    [Fact]
    public void Interpolate_LinearPerChannelWithJsRounding()
    {
        // JS Math.round 半值远离零：0 + (171-0)*0.5 = 85.5 → 86；21.5 → 22；31.5 → 32
        var from = Color.FromRgb(0, 0, 0);
        var to = Color.FromRgb(171, 43, 63);
        Assert.Equal(Color.FromRgb(86, 22, 32), SpinnerMath.Interpolate(from, to, 0.5));
        Assert.Equal(from, SpinnerMath.Interpolate(from, to, 0));
        Assert.Equal(to, SpinnerMath.Interpolate(from, to, 1));
    }

    [Fact]
    public void Interpolate_NonRgb_FallsBackToDiscrete()
    {
        // 非 Rgb 色无法逐通道插值 — t 过半取目标色（对齐基准 parseRGB null 的离散回退语义）
        var from = Color.Default;
        var to = Color.FromRgb(1, 2, 3);
        Assert.Equal(from, SpinnerMath.Interpolate(from, to, 0.49));
        Assert.Equal(to, SpinnerMath.Interpolate(from, to, 0.5));
    }

    [Fact]
    public void Glimmer_DefaultMode_MovesLeftwardAt200Ms()
    {
        // 非 requesting：glimmerIndex = width + 10 - pos % (width+20)，随时间递减（左移）
        Assert.Equal(20, SpinnerMath.GlimmerIndex(SpinnerMode.Responding, 0, messageWidth: 10, isStalled: false, reducedMotion: false));
        Assert.Equal(19, SpinnerMath.GlimmerIndex(SpinnerMode.Responding, 200, 10, false, false));
        Assert.Equal(0, SpinnerMath.GlimmerIndex(SpinnerMode.Responding, 4000, 10, false, false));
        Assert.Equal(-9, SpinnerMath.GlimmerIndex(SpinnerMode.Responding, 5800, 10, false, false));
        // pos=30 → 30%30=0 → 回到 20（周期回绕）
        Assert.Equal(20, SpinnerMath.GlimmerIndex(SpinnerMode.Responding, 6000, 10, false, false));
    }

    [Fact]
    public void Glimmer_Requesting_MovesRightwardAt50Ms()
    {
        // requesting：glimmerIndex = pos % (width+20) - 10，随时间递增（右移），50ms/步
        Assert.Equal(-10, SpinnerMath.GlimmerIndex(SpinnerMode.Requesting, 0, 10, false, false));
        Assert.Equal(-9, SpinnerMath.GlimmerIndex(SpinnerMode.Requesting, 50, 10, false, false));
        Assert.Equal(10, SpinnerMath.GlimmerIndex(SpinnerMode.Requesting, 1000, 10, false, false));
        Assert.Equal(-10, SpinnerMath.GlimmerIndex(SpinnerMode.Requesting, 1500, 10, false, false));
    }

    [Fact]
    public void Glimmer_StalledOrReducedMotion_IsOff()
    {
        Assert.Equal(-100, SpinnerMath.GlimmerIndex(SpinnerMode.Responding, 1000, 10, isStalled: true, reducedMotion: false));
        Assert.Equal(-100, SpinnerMath.GlimmerIndex(SpinnerMode.Responding, 1000, 10, false, reducedMotion: true));
    }

    [Fact]
    public void Flash_ToolUseSinePeriod2s()
    {
        // flashOpacity = (sin(time/1000·π)+1)/2：t=0 → 0.5，t=500 → 1，t=1500 → 0
        Assert.Equal(0.5, SpinnerMath.FlashOpacity(SpinnerMode.ToolUse, 0, reducedMotion: false), precision: 10);
        Assert.Equal(1.0, SpinnerMath.FlashOpacity(SpinnerMode.ToolUse, 500, false), precision: 10);
        Assert.Equal(0.0, SpinnerMath.FlashOpacity(SpinnerMode.ToolUse, 1500, false), precision: 10);
        // 非 tool-use 或 reducedMotion 恒 0
        Assert.Equal(0, SpinnerMath.FlashOpacity(SpinnerMode.Responding, 500, false));
        Assert.Equal(0, SpinnerMath.FlashOpacity(SpinnerMode.ToolUse, 500, reducedMotion: true));
    }
}

/// <summary>停滞状态机测试 — 对齐基准 useStalledAnimation（动画时钟驱动，非墙钟）。</summary>
public sealed class StallTrackerTests
{
    [Fact]
    public void TokensFlowing_NeverStalled()
    {
        var tracker = new StallTracker();
        // 每 50ms 一帧、每 10 帧流入新 token — 全程不停滞
        for (var frame = 0; frame <= 60; frame++)
        {
            var (isStalled, intensity) = tracker.Tick(frame * 50, responseLength: frame / 10, hasActiveTools: false, reducedMotion: false);
            Assert.False(isStalled);
            Assert.Equal(0, intensity);
        }
    }

    [Fact]
    public void NoTokens_StallsAfter3s_ThenSmooths()
    {
        var tracker = new StallTracker();
        for (var time = 0; time <= 3000; time += 50)
        {
            var (isStalled, _) = tracker.Tick(time, 0, false, false);
            Assert.False(isStalled);
        }

        // t=3050：越过 3s 阈值 — 原始 intensity=0.025，平滑后每 50ms 步进 10% → 0.0025
        var (stalled, intensity) = tracker.Tick(3050, 0, false, false);
        Assert.True(stalled);
        Assert.Equal(0.0025, intensity, precision: 12);

        // t=3100：原始 0.05，current = 0.0025 + (0.05-0.0025)*0.1 = 0.00725
        var (_, next) = tracker.Tick(3100, 0, false, false);
        Assert.Equal(0.00725, next, precision: 12);
    }

    [Fact]
    public void NoTokens_ReducedMotion_InstantIntensity()
    {
        var tracker = new StallTracker();
        tracker.Tick(0, 0, false, reducedMotion: true);
        var (isStalled, intensity) = tracker.Tick(3050, 0, false, true);
        Assert.True(isStalled);
        Assert.Equal(0.025, intensity, precision: 12);
    }

    [Fact]
    public void HasActiveTools_PreventsStall_ButZeroTokensStillCountFromMount()
    {
        var tracker = new StallTracker();
        tracker.Tick(0, 0, false, false);
        // 工具活跃期间：timeSinceLastToken 恒 0，不停滞
        var (duringTools, _) = tracker.Tick(3050, 0, hasActiveTools: true, reducedMotion: false);
        Assert.False(duringTools);
        // 基准 else 分支：responseLength==0 → 从 mountTime 起算（lastTokenTime 刷新无效）
        var (afterTools, _) = tracker.Tick(4000, 0, false, false);
        Assert.True(afterTools);
    }

    [Fact]
    public void HasActiveTools_RefreshesLastTokenTime_WhenTokensExist()
    {
        var tracker = new StallTracker();
        tracker.Tick(0, 100, false, false);
        // 工具活跃刷新 lastTokenTime=3050
        var (duringTools, _) = tracker.Tick(3050, 100, hasActiveTools: true, reducedMotion: false);
        Assert.False(duringTools);
        // responseLength>0 → 从 lastTokenTime 起算：4000-3050=950 < 3000，未停滞
        var (afterTools, _) = tracker.Tick(4000, 100, false, false);
        Assert.False(afterTools);
    }

    [Fact]
    public void NewTokens_ResetStallImmediately()
    {
        var tracker = new StallTracker();
        tracker.Tick(0, 0, false, false);
        var (stalled, _) = tracker.Tick(3050, 0, false, false);
        Assert.True(stalled);
        // 新 token 到达：lastTokenTime/stalledIntensity/lastSmoothTime 全部重置
        var (reset, intensity) = tracker.Tick(3100, 100, false, false);
        Assert.False(reset);
        Assert.Equal(0, intensity);
    }

    [Fact]
    public void TokensFromMount_StallsFromLastTokenTime()
    {
        var tracker = new StallTracker();
        tracker.Tick(0, 50, false, false);
        // responseLength>0 → 从 lastTokenTime（=mount 0）起算：3100 > 3000 停滞
        var (isStalled, _) = tracker.Tick(3100, 50, false, false);
        Assert.True(isStalled);
    }
}

/// <summary>token 计数平滑测试 — 对齐基准 SpinnerAnimationRow 平滑增量（3/8..15%/50）。</summary>
public sealed class TokenCounterTests
{
    [Fact]
    public void SeedsWithCurrentLengthOnFirstTick()
    {
        var counter = new TokenCounter();
        Assert.Equal(100, counter.Tick(100, reducedMotion: false));
    }

    [Fact]
    public void SmallGap_IncrementsByThree()
    {
        // gap 相对上一帧显示值：3 → +3；69（<70 边界）→ 仍 +3
        Assert.Equal(103, CatchUp(100, 103));
        Assert.Equal(103, CatchUp(100, 169));
    }

    [Fact]
    public void MediumGap_AtLeast8Or15Percent()
    {
        // gap=70 → max(8, ceil(10.5)=11) = 11
        Assert.Equal(111, CatchUp(100, 170));
        // gap=100 → max(8, 15) = 15
        Assert.Equal(115, CatchUp(100, 200));
        // gap=199 → max(8, ceil(29.85)=30) = 30
        Assert.Equal(130, CatchUp(100, 299));
    }

    [Fact]
    public void LargeGap_IncrementsBy50_ClampedToCurrent()
    {
        var counter = new TokenCounter();
        counter.Tick(100, false);
        // gap=200 → +50
        Assert.Equal(150, counter.Tick(300, false));
        // gap=950 → +50，且 min 钳制不越过当前值
        Assert.Equal(200, counter.Tick(1100, false));
    }

    [Fact]
    public void ReducedMotion_TracksInstantly()
    {
        var counter = new TokenCounter();
        counter.Tick(100, reducedMotion: true);
        Assert.Equal(1000, counter.Tick(1000, reducedMotion: true));
    }

    private static int CatchUp(int from, int to)
    {
        var counter = new TokenCounter();
        counter.Tick(from, false);
        return counter.Tick(to, false);
    }
}

/// <summary>SpinnerAnimator 门面测试 — 组合 frame/stall/glimmer/flash/token 为单一状态。</summary>
public sealed class SpinnerAnimatorTests
{
    [Fact]
    public void Tick_CombinesAllDerivations()
    {
        var animator = new SpinnerAnimator();
        for (var time = 0; time <= 3000; time += 50)
        {
            animator.Tick(time, responseLength: 0, hasActiveTools: false, reducedMotion: false, SpinnerMode.Responding, messageWidth: 10);
        }

        var state = animator.Tick(3050, 0, false, false, SpinnerMode.Responding, 10);
        Assert.Equal(25, state.Frame);
        Assert.True(state.IsStalled);
        Assert.Equal(0.0025, state.StalledIntensity, precision: 12);
        // 停滞时 shimmer 熄灭
        Assert.Equal(-100, state.GlimmerIndex);
        Assert.Equal(0, state.FlashOpacity);
        Assert.Equal(0, state.DisplayedResponseLength);
    }

    [Fact]
    public void Tick_Requesting_GlimmerMovesRightward()
    {
        var animator = new SpinnerAnimator();
        var state = animator.Tick(1000, 100, false, false, SpinnerMode.Requesting, 10);
        Assert.Equal(8, state.Frame);
        Assert.False(state.IsStalled);
        Assert.Equal(10, state.GlimmerIndex);
        Assert.Equal(100, state.DisplayedResponseLength);
    }

    [Fact]
    public void Tick_ReducedMotion_AllStatic()
    {
        var animator = new SpinnerAnimator();
        var state = animator.Tick(1000, 100, false, reducedMotion: true, SpinnerMode.ToolUse, 10);
        Assert.Equal(0, state.Frame);
        Assert.Equal(-100, state.GlimmerIndex);
        Assert.Equal(0, state.FlashOpacity);
        Assert.Equal(100, state.DisplayedResponseLength);
    }
}
