namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// 全局运行状态条 VM 测试 — 随机动词/耗时/token 聚合/后台代理计数，
/// 以及卡死检测状态机（Monitoring → Stalled，心跳复位；规则8风格，时钟注入可测）。
/// </summary>
public class GlobalRunStatusViewModelTests {
    private sealed class MutableClock {
        public DateTime Now = new(2026, 8, 26, 12, 0, 0);
        public void AdvanceSeconds(int s) => Now = Now.AddSeconds(s);
    }

    private static (GlobalRunStatusViewModel Vm, MutableClock Clock, Action Tick) Create() {
        var clock = new MutableClock();
        var vm = new GlobalRunStatusViewModel(() => clock.Now);
        // 定时器回调由测试手动驱动（替代 DispatcherTimer）
        return (vm, clock, () => vm.OnHeartbeatTick());
    }

    [Fact]
    public void StartTurn_ShouldSampleVerb_AndResetState() {
        var (vm, _, _) = Create();

        vm.StartTurn();
        vm.Verb.Should().NotBeNullOrEmpty("回合开始必须采样随机动词");
        vm.IsBusy.Should().BeTrue();
        vm.IsStalled.Should().BeFalse();
        vm.ElapsedText.Should().Contain("0");
        vm.TokenText.Should().BeEmpty();
        vm.StatusGlyph.Should().Be("⟳");
    }

    [Fact]
    public void AddTokens_ShouldFormatThousands() {
        var (vm, _, _) = Create();
        vm.StartTurn();

        vm.AddTokens(48200);
        vm.TokenText.Should().Contain("48").And.Contain("k").And.Contain("tokens");
    }

    [Fact]
    public void Heartbeat_ActiveTool_ShouldSuppressStall() {
        var (vm, clock, tick) = Create();
        vm.StartTurn();
        vm.ReportActivity(hasActiveTool: true);

        clock.AdvanceSeconds(10);
        tick();

        vm.IsStalled.Should().BeFalse("有工具执行时豁免卡死检测（对齐 TS 原版）");
    }

    [Fact]
    public void Heartbeat_NoActivityBeyondThreshold_ShouldTransitionToStalled() {
        var (vm, clock, tick) = Create();
        vm.StartTurn();
        vm.ReportActivity(hasActiveTool: false);

        clock.AdvanceSeconds(2);
        tick();
        vm.IsStalled.Should().BeFalse("阈值内不判卡死");

        clock.AdvanceSeconds(4);
        tick();
        vm.IsStalled.Should().BeTrue(">3s 无心跳且无活跃工具应进入卡死态");
    }

    [Fact]
    public void Heartbeat_NewActivity_ShouldResetToNormal() {
        var (vm, clock, tick) = Create();
        vm.StartTurn();
        vm.ReportActivity(false);
        clock.AdvanceSeconds(5);
        tick();
        vm.IsStalled.Should().BeTrue();

        vm.ReportActivity(true); // 新事件到达
        tick();
        vm.IsStalled.Should().BeFalse("新心跳应复位卡死态");
    }

    [Fact]
    public void EndTurn_ShouldStopStall_AndFreezeElapsed() {
        var (vm, clock, tick) = Create();
        vm.StartTurn();
        vm.ReportActivity(false);
        clock.AdvanceSeconds(5);
        vm.EndTurn();
        tick();

        vm.IsBusy.Should().BeFalse();
        vm.IsStalled.Should().BeFalse("回合结束后不得显示卡死");
        vm.StatusGlyph.Should().Be("✓", "结束定格成功态");
    }

    [Fact]
    public void SetBackgroundCount_ShouldDriveVisibility() {
        var (vm, _, _) = Create();
        vm.SetBackgroundCount(0);
        vm.BackgroundPillText.Should().BeEmpty();

        vm.SetBackgroundCount(2);
        vm.BackgroundPillText.Should().Contain("2").And.Contain("后台");
    }

    // === MarqueeText 拼接逻辑测试（缺失点2补全） ===

    [Fact]
    public void MarqueeText_Idle_ShowsDefaultWelcome() {
        var (vm, _, _) = Create();
        vm.MarqueeText.Should().Contain("JoinCode Ava 就绪",
            "空闲时应显示默认欢迎语引导用户操作");
    }

    [Fact]
    public void MarqueeText_Busy_ShouldContainVerb() {
        var (vm, _, _) = Create();
        vm.StartTurn();
        vm.MarqueeText.Should().Contain(vm.Verb,
            "忙碌时走马灯应包含当前回合采样的动词");
    }

    [Fact]
    public void MarqueeText_WithActivity_ShouldContainActivityLabel() {
        var (vm, _, _) = Create();
        vm.StartTurn();
        vm.ReportActivity(hasActiveTool: true, label: "bash");
        vm.MarqueeText.Should().Contain("bash",
            "走马灯应包含最近活动标签（工具名/子代理名）");
    }

    [Fact]
    public void MarqueeText_WithTokens_ShouldContainFormattedTokens() {
        var (vm, _, _) = Create();
        vm.StartTurn();
        vm.AddTokens(5000);
        vm.MarqueeText.Should().Contain("5k").And.Contain("tokens",
            "走马灯应包含格式化后的 token 消耗");
    }

    [Fact]
    public void MarqueeText_WithElapsed_ShouldContainElapsedSeconds() {
        var (vm, clock, _) = Create();
        vm.StartTurn();
        clock.AdvanceSeconds(5);
        vm.OnHeartbeatTick();
        vm.MarqueeText.Should().Contain("5",
            "走马灯应包含回合耗时");
    }

    [Fact]
    public void MarqueeText_AllPartsJoinedBySeparator() {
        var (vm, clock, _) = Create();
        vm.StartTurn();
        vm.ReportActivity(false, "grep");
        clock.AdvanceSeconds(3);
        vm.OnHeartbeatTick();
        vm.AddTokens(2000);
        vm.MarqueeText.Should().Contain("·",
            "多段内容应用分隔符连接");
    }

    // === MarqueeStopped 事件触发测试（缺失点3补全） ===

    [Fact]
    public void MarqueeStopped_EndTurnNormal_ShouldFireNormal() {
        var (vm, _, _) = Create();
        vm.StartTurn();
        MarqueeStopReason? fired = null;
        vm.MarqueeStopped += r => fired = r;
        vm.EndTurn(MarqueeStopReason.Normal);
        fired.Should().Be(MarqueeStopReason.Normal,
            "正常结束应触发 Normal 原因");
    }

    [Fact]
    public void MarqueeStopped_EndTurnUserAborted_ShouldFireUserAborted() {
        var (vm, _, _) = Create();
        vm.StartTurn();
        MarqueeStopReason? fired = null;
        vm.MarqueeStopped += r => fired = r;
        vm.EndTurn(MarqueeStopReason.UserAborted);
        fired.Should().Be(MarqueeStopReason.UserAborted,
            "用户终止应触发 UserAborted 原因");
    }

    [Fact]
    public void MarqueeStopped_EndTurnAbnormal_ShouldFireAbnormal() {
        var (vm, _, _) = Create();
        vm.StartTurn();
        MarqueeStopReason? fired = null;
        vm.MarqueeStopped += r => fired = r;
        vm.EndTurn(MarqueeStopReason.Abnormal);
        fired.Should().Be(MarqueeStopReason.Abnormal,
            "异常结束应触发 Abnormal 原因");
    }

    [Fact]
    public void MarqueeStopped_StalledState_ShouldFireStalled() {
        var (vm, clock, tick) = Create();
        MarqueeStopReason? fired = null;
        vm.MarqueeStopped += r => fired = r;
        vm.StartTurn();
        vm.ReportActivity(hasActiveTool: false);
        clock.AdvanceSeconds(5);
        tick();
        fired.Should().Be(MarqueeStopReason.Stalled,
            "状态机进入 Stalled 应自动触发 MarqueeStopped 事件");
    }

    [Fact]
    public void MarqueeStopped_EndTurnWhenNotBusy_ShouldNotFire() {
        var (vm, _, _) = Create();
        MarqueeStopReason? fired = null;
        vm.MarqueeStopped += r => fired = r;
        vm.EndTurn(MarqueeStopReason.Abnormal);
        fired.Should().BeNull(
            "非忙碌态 EndTurn 不应触发事件（wasBusy=false 守卫）");
    }
}