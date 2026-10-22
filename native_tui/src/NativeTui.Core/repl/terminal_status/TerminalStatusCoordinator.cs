namespace NativeTui.Core.Repl;

/// <summary>
/// 终端状态协调器（A5/A6/A8 — ReplShell 每拍三态驱动的三路输出统一组件）：
/// 会话三态（waiting &gt; busy &gt; idle）变更 → ① 标题（TerminalTitleTracker — busy 动画前缀）
/// ② OSC 21337 tab 状态 preset（tabStatusEnabled 门控）③ OSC 9;4 任务栏进度
///（progressEnabled 门控：进入 busy = indeterminate(state 3)、离开 busy = clear(state 0) — 回合进行中保持不清）。
/// Shutdown 退出三路对称清理（标题空 + CLEAR_TAB_STATUS + 进度 clear）。
/// 门控默认关闭由宿主开启（交互模式显式启用 — golden/快照测试输出流零污染契约）。
/// </summary>
public sealed class TerminalStatusCoordinator
{
    private readonly TextWriter _output;
    private readonly TerminalTitleTracker? _titleTracker;
    private readonly bool _tabStatusEnabled;
    private readonly bool _progressEnabled;
    private TerminalSessionStatus? _status;

    /// <summary>进度条当前显示态（busy 进入置位写 indeterminate；到达 idle 才复位写 clear — waiting 挂起不清）。</summary>
    private bool _progressShown;

    /// <summary>
    /// 构造终端状态协调器。
    /// </summary>
    /// <param name="output">终端输出流（tab 状态/进度序列写入通道）。</param>
    /// <param name="titleTracker">标题追踪器（null = 标题能力降级，tab/进度两路不受影响）。</param>
    /// <param name="tabStatusEnabled">21337 tab 状态门控（默认关闭 — 宿主交互模式开启）。</param>
    /// <param name="progressEnabled">OSC 9;4 进度门控（宿主经 IsProgressReportingAvailable 判定后传入）。</param>
    public TerminalStatusCoordinator(
        TextWriter output,
        TerminalTitleTracker? titleTracker,
        bool tabStatusEnabled = false,
        bool progressEnabled = false)
    {
        _output = output;
        _titleTracker = titleTracker;
        _tabStatusEnabled = tabStatusEnabled;
        _progressEnabled = progressEnabled;
    }

    /// <summary>会话三态变更（等值去抖）— 三路输出 + busy 进度边沿（进入 indeterminate / 回合真结束 idle 才 clear；
    /// waiting 挂起不清 — 权限确认期间任务仍在进行，进度条保持）。</summary>
    /// <param name="status">目标态（调用方完成 waiting&gt;busy&gt;idle 优先级归并）。</param>
    public void SetStatus(TerminalSessionStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        _titleTracker?.SetStatus(status);
        if (_tabStatusEnabled)
        {
            _output.Write(status.ToTabStatusSequence());
        }

        if (_progressEnabled)
        {
            if (status == TerminalSessionStatus.Busy && !_progressShown)
            {
                _progressShown = true;
                _output.Write(Osc.Progress(state: 3, progress: 0));
            }
            else if (status == TerminalSessionStatus.Idle && _progressShown)
            {
                _progressShown = false;
                _output.Write(Osc.Progress(state: 0, progress: 0));
            }
        }
    }

    /// <summary>焦点变更转发（DEC 1004 CSI I/O 驱动 — 标题动画暂停/恢复）。</summary>
    /// <param name="focused">true = 聚焦/未知；false = 失焦。</param>
    public void SetFocused(bool focused) => _titleTracker?.SetFocused(focused);

    /// <summary>动画时钟转发（主循环每拍 — 标题动画帧推进）。</summary>
    /// <param name="deltaMs">本拍间隔。</param>
    public void Tick(int deltaMs) => _titleTracker?.Tick(deltaMs);

    /// <summary>退出清理三路对称：标题空还原 + CLEAR_TAB_STATUS + 进度 clear（门控路同样清理 — 关闭前曾开启过的残留防御）。</summary>
    public void Shutdown()
    {
        _titleTracker?.Clear();
        if (_tabStatusEnabled)
        {
            _output.Write(Osc.ClearTabStatus());
        }

        if (_progressEnabled)
        {
            _output.Write(Osc.Progress(state: 0, progress: 0));
        }
    }
}
