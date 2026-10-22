namespace NativeTui.Tests.Repl;

/// <summary>
/// 终端标题状态机测试（A2 — 对齐基准 REPL.tsx AnimatedTerminalTitle + use-terminal-title.ts）：
/// 三态优先级 waiting&gt;busy&gt;idle 驱动；busy 动画帧 ⠂/⠐ 每 960ms 循环、静态前缀 ✳；
/// 失焦暂停动画（帧冻结）；状态/帧/焦点变化才写（去抖）；disabled 零写；Clear 写空标题还原。
/// 21337 tab 状态 preset（TerminalSessionStatus 扩展 — 唯一数据源禁止散落硬编码 RGB）一并锁定。
/// </summary>
public sealed class TerminalTitleTrackerTests
{
    /// <summary>标题写入捕获器（委托注入 — Core 零依赖不持平台 API，对齐 clipboard 注入先例）。</summary>
    private sealed class TitleCapture
    {
        public readonly List<string> Writes = [];
        public void Set(string title) => Writes.Add(title);
        public string? Last => Writes.Count > 0 ? Writes[^1] : null;
    }

    private static (TerminalTitleTracker Tracker, TitleCapture Capture) Create(bool disabled = false) =>
        Create("JoinCode", disabled);

    private static (TerminalTitleTracker Tracker, TitleCapture Capture) Create(string title, bool disabled = false)
    {
        var capture = new TitleCapture();
        return (new TerminalTitleTracker(title, capture.Set, disabled), capture);
    }

    // ---- 初始/状态写入 ----

    [Fact]
    public void InitialStatus_Idle_WritesStaticPrefix()
    {
        // 初始 idle：静态前缀 ✳ + 标题（基准 prefix = isAnimating ? 动画帧 : '✳'）
        var (tracker, capture) = Create("JoinCode");
        tracker.SetStatus(TerminalSessionStatus.Idle);
        Assert.Equal("✳ JoinCode", capture.Last);
        Assert.Single(capture.Writes);
    }

    [Fact]
    public void Busy_WritesFirstAnimationFrame()
    {
        // busy（isAnimating）：动画首帧 ⠂ 前缀
        var (tracker, capture) = Create("JoinCode");
        tracker.SetStatus(TerminalSessionStatus.Busy);
        Assert.Equal("⠂ JoinCode", capture.Last);
    }

    [Fact]
    public void Waiting_WritesStaticPrefix_NotAnimationFrame()
    {
        // waiting（等权限确认）非动画 — 静态 ✳（基准 isAnimating 仅 isLoading）
        var (tracker, capture) = Create("JoinCode");
        tracker.SetStatus(TerminalSessionStatus.Waiting);
        Assert.Equal("✳ JoinCode", capture.Last);
    }

    [Fact]
    public void SameStatus_NoRewrite()
    {
        // 状态无变化零写（useEffect 依赖等值不触发）
        var (tracker, capture) = Create();
        tracker.SetStatus(TerminalSessionStatus.Idle);
        var countAfterFirst = capture.Writes.Count;
        tracker.SetStatus(TerminalSessionStatus.Idle);
        Assert.Equal(countAfterFirst, capture.Writes.Count);
    }

    // ---- 动画帧推进 ----

    [Fact]
    public void Busy_BelowFrameInterval_KeepsFirstFrame()
    {
        // <960ms 同帧 ⠂（每拍 50ms × 19 = 950ms）
        var (tracker, capture) = Create();
        tracker.SetStatus(TerminalSessionStatus.Busy);
        for (var i = 0; i < 19; i++)
        {
            tracker.Tick(50);
        }

        Assert.Equal("⠂ JoinCode", capture.Last);
    }

    [Fact]
    public void Busy_AcrossFrameInterval_AdvancesToSecondFrame()
    {
        // ≥960ms 换帧 ⠐（960/50 = 19.2 拍 → 20 拍过界）
        var (tracker, capture) = Create();
        tracker.SetStatus(TerminalSessionStatus.Busy);
        for (var i = 0; i < 20; i++)
        {
            tracker.Tick(50);
        }

        Assert.Equal("⠐ JoinCode", capture.Last);
    }

    [Fact]
    public void Busy_TwoIntervals_CyclesBackToFirstFrame()
    {
        // 2×960ms 循环回 ⠂（帧数组只有两帧 — REPL.tsx TITLE_ANIMATION_FRAMES = ['⠂','⠐']）
        // 38 拍 = 1900ms → 帧 1 = ⠐；第 39 拍 = 1950ms → 帧 2%2=0 回 ⠂
        var (tracker, capture) = Create();
        tracker.SetStatus(TerminalSessionStatus.Busy);
        for (var i = 0; i < 38; i++)
        {
            tracker.Tick(50);
        }

        Assert.Equal("⠐ JoinCode", capture.Last);
        tracker.Tick(50);
        Assert.Equal("⠂ JoinCode", capture.Last);
    }

    [Fact]
    public void Idle_Tick_NoRewrite()
    {
        // 非 busy tick 不写（无动画无变化）
        var (tracker, capture) = Create();
        tracker.SetStatus(TerminalSessionStatus.Idle);
        var count = capture.Writes.Count;
        tracker.Tick(50);
        tracker.Tick(50);
        Assert.Equal(count, capture.Writes.Count);
    }

    // ---- 失焦暂停 ----

    [Fact]
    public void Blurred_FreezesAnimationFrame()
    {
        // 失焦暂停动画（基准 terminalFocused=false 不启 interval）— blurred 期间累计不推进
        var (tracker, capture) = Create();
        tracker.SetStatus(TerminalSessionStatus.Busy);
        tracker.SetFocused(false);
        var frozenTitle = capture.Last;
        for (var i = 0; i < 40; i++)
        {
            tracker.Tick(50);
        }

        // 帧冻结不换（2000ms >> 960ms 阈值也不动）
        Assert.Equal(frozenTitle, capture.Last);
        // 恢复焦点继续推进 — 下一帧可达
        tracker.SetFocused(true);
        for (var i = 0; i < 20; i++)
        {
            tracker.Tick(50);
        }

        Assert.Equal("⠐ JoinCode", capture.Last);
    }

    [Fact]
    public void FocusChange_Idle_NoRewrite()
    {
        // 焦点变化在非动画态不触发写（标题内容与焦点无关 — 只有动画受焦点影响）
        var (tracker, capture) = Create();
        tracker.SetStatus(TerminalSessionStatus.Idle);
        var count = capture.Writes.Count;
        tracker.SetFocused(false);
        tracker.SetFocused(true);
        Assert.Equal(count, capture.Writes.Count);
    }

    // ---- 禁用 / 清理 ----

    [Fact]
    public void Disabled_NeverWrites()
    {
        // CLAUDE_CODE_DISABLE_TERMINAL_TITLE 语义 — 宿主读 env 传 disabled，tracker 零写
        var (tracker, capture) = Create(disabled: true);
        tracker.SetStatus(TerminalSessionStatus.Busy);
        tracker.Tick(50);
        tracker.SetFocused(false);
        tracker.Clear();
        Assert.Empty(capture.Writes);
    }

    [Fact]
    public void Clear_WritesEmptyTitle()
    {
        // 退出还原 — 基准 CLEAR_TERMINAL_TITLE = OSC 0 空标题（Windows 宿主自行还原 Console.Title 快照）
        var (tracker, capture) = Create();
        tracker.SetStatus(TerminalSessionStatus.Busy);
        tracker.Clear();
        Assert.Equal(string.Empty, capture.Last);
    }

    // ---- 21337 tab 状态 preset（枚举扩展 — A5 接线的唯一数据源）----

    [Theory]
    [InlineData(TerminalSessionStatus.Idle, "\x1b]21337;indicator=#00d75f;status=Idle;status-color=#888888\x07")]
    [InlineData(TerminalSessionStatus.Busy, "\x1b]21337;indicator=#ff9500;status=Working…;status-color=#ff9500\x07")]
    [InlineData(TerminalSessionStatus.Waiting, "\x1b]21337;indicator=#5f87ff;status=Waiting;status-color=#5f87ff\x07")]
    public void TabStatusPreset_MatchesBaselineColors(TerminalSessionStatus status, string expected)
    {
        // 基准 use-tab-status.ts TAB_STATUS_PRESETS 逐色对齐（idle 绿/灰、busy 橙、waiting 蓝）
        Assert.Equal(expected, status.ToTabStatusSequence());
    }
}
