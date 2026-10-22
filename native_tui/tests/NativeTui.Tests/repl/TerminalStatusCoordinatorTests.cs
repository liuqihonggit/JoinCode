namespace NativeTui.Tests.Repl;

/// <summary>
/// 终端状态协调器测试（A5/A6/A8 — ReplShell 每拍三态驱动的组件层）：
/// 三态变更 → 标题 tracker + 21337 tab 序列 + OSC 9;4 进度三路输出；
/// busy 进入写 indeterminate、离开写 clear；Shutdown 对称清理（标题空 + CLEAR_TAB_STATUS + 进度 clear）；
/// tabStatus/progress 门控关闭零输出（默认 — 现有 golden 测试零污染契约）。
/// </summary>
public sealed class TerminalStatusCoordinatorTests
{
    /// <summary>标题写入捕获。</summary>
    private sealed class TitleCapture
    {
        public readonly List<string> Writes = [];
        public void Set(string title) => Writes.Add(title);
    }

    private static (TerminalStatusCoordinator Coordinator, TitleCapture Titles, StringWriter Output) Create(
        bool tabStatus = true, bool progress = true, bool titleDisabled = false)
    {
        var titles = new TitleCapture();
        var output = new StringWriter();
        var tracker = new TerminalTitleTracker("JoinCode", titles.Set, titleDisabled);
        return (new TerminalStatusCoordinator(output, tracker, tabStatus, progress), titles, output);
    }

    [Fact]
    public void SetStatus_Busy_WritesTitleIndeterminateProgressAndTabPreset()
    {
        // busy 三路：标题 ⠂ 动画前缀 + 21337 busy preset + OSC 9;4 state=3（indeterminate）
        var (coordinator, titles, output) = Create();
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        Assert.Equal("⠂ JoinCode", titles.Writes[^1]);
        Assert.Contains("\x1b]21337;indicator=#ff9500;status=Working…;status-color=#ff9500\x07", output.ToString());
        Assert.Contains("\x1b]9;4;3;0\x07", output.ToString());
    }

    [Fact]
    public void SetStatus_Idle_WritesTitleAndTabButNoProgress()
    {
        // idle：标题 ✳ + 21337 idle preset；初始进入非 busy 不发进度（回合从未开始）
        var (coordinator, titles, output) = Create();
        coordinator.SetStatus(TerminalSessionStatus.Idle);
        Assert.Equal("✳ JoinCode", titles.Writes[^1]);
        Assert.Contains("\x1b]21337;indicator=#00d75f;status=Idle;status-color=#888888\x07", output.ToString());
        Assert.DoesNotContain("\x1b]9;4", output.ToString());
    }

    [Fact]
    public void SetStatus_BusyToIdle_ClearsProgress()
    {
        // 离开 busy → 进度 clear（state=0 — 回合结束清任务栏进度）
        var (coordinator, _, output) = Create();
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        var progressWrites = ProgressCount(output);
        coordinator.SetStatus(TerminalSessionStatus.Idle);
        Assert.Equal(progressWrites + 1, ProgressCount(output));
        Assert.Contains("\x1b]9;4;0;0\x07", output.ToString());
    }

    [Fact]
    public void SetStatus_SameStatus_NoRewrite()
    {
        // 状态等值零写（去抖 — 三路全静默）
        var (coordinator, titles, output) = Create();
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        var titleCount = titles.Writes.Count;
        var outputLength = output.ToString().Length;
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        Assert.Equal(titleCount, titles.Writes.Count);
        Assert.Equal(outputLength, output.ToString().Length);
    }

    [Fact]
    public void SetStatus_Waiting_TabPresetWaiting_NoProgressChange()
    {
        // busy → waiting（权限挂起）：tab 切 waiting preset；进度保持 indeterminate 不清（回合仍在进行）
        var (coordinator, _, output) = Create();
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        coordinator.SetStatus(TerminalSessionStatus.Waiting);
        Assert.Contains("\x1b]21337;indicator=#5f87ff;status=Waiting;status-color=#5f87ff\x07", output.ToString());
        Assert.DoesNotContain("\x1b]9;4;0", output.ToString());
    }

    [Fact]
    public void SetFocused_ForwardsToTitleTracker()
    {
        // 焦点转发 — 标题动画暂停/恢复
        var (coordinator, titles, _) = Create();
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        coordinator.SetFocused(false);
        var frozen = titles.Writes[^1];
        coordinator.Tick(50);
        Assert.Equal(frozen, titles.Writes[^1]);
    }

    [Fact]
    public void Tick_ForwardsToTitleTracker()
    {
        // 动画时钟转发 — 960ms 换帧
        var (coordinator, titles, _) = Create();
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        for (var i = 0; i < 20; i++)
        {
            coordinator.Tick(50);
        }

        Assert.Equal("⠐ JoinCode", titles.Writes[^1]);
    }

    [Fact]
    public void Shutdown_WritesEmptyTitleClearTabAndClearProgress()
    {
        // 退出清理三路对称：标题空 + CLEAR_TAB_STATUS + 进度 clear
        var (coordinator, titles, output) = Create();
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        coordinator.Shutdown();
        Assert.Equal(string.Empty, titles.Writes[^1]);
        Assert.Contains("\x1b]21337;indicator=;status=;status-color=\x07", output.ToString());
        Assert.Contains("\x1b]9;4;0;0\x07", output.ToString());
    }

    [Fact]
    public void TabStatusDisabled_ZeroTabOutput()
    {
        // tab 门控关闭 — 输出流零 21337（golden 测试零污染契约）
        var (coordinator, _, output) = Create(tabStatus: false);
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        coordinator.Shutdown();
        Assert.DoesNotContain("21337", output.ToString());
    }

    [Fact]
    public void ProgressDisabled_ZeroProgressOutput()
    {
        // 进度门控关闭 — 输出流零 9;4（能力判定在宿主层，组件只看开关）
        var (coordinator, _, output) = Create(progress: false);
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        coordinator.Shutdown();
        Assert.DoesNotContain("9;4", output.ToString());
    }

    [Fact]
    public void NullTitleTracker_StillWritesTabAndProgress()
    {
        // 标题能力降级（null tracker）不影响 tab/进度两路
        var output = new StringWriter();
        var coordinator = new TerminalStatusCoordinator(output, titleTracker: null, tabStatusEnabled: true, progressEnabled: true);
        coordinator.SetStatus(TerminalSessionStatus.Busy);
        Assert.Contains("21337", output.ToString());
        Assert.Contains("9;4;3;0", output.ToString());
    }

    private static int ProgressCount(StringWriter output)
    {
        var text = output.ToString();
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf("\x1b]9;4", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += 5;
        }

        return count;
    }
}
