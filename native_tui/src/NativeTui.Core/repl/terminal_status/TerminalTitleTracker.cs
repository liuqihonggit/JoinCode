namespace NativeTui.Core.Repl;

/// <summary>
/// 终端标题状态机（A2 — 对齐基准 REPL.tsx:484 AnimatedTerminalTitle + ink/hooks/use-terminal-title.ts）：
/// busy 动画前缀 ⠂/⠐ 每 960ms 循环、其余静态前缀 ✳；失焦暂停动画（帧冻结，恢复继续）；
/// 状态/帧变化才经 setTitle 委托写（去抖 — useEffect 依赖等值不触发语义）。
/// 写通道宿主装配（Core 零依赖不持平台 API，对齐 clipboard 注入先例）：
/// Windows → Console.Title（= 基准 process.title）；Unix → Osc.SetTitle 经 stdout。
/// 禁用（基准 CLAUDE_CODE_DISABLE_TERMINAL_TITLE truthy）由宿主读 env 传 disabled — 零写。
/// </summary>
public sealed class TerminalTitleTracker
{
    /// <summary>动画帧序列（基准 REPL.tsx:473 TITLE_ANIMATION_FRAMES = ['⠂','⠐']）。</summary>
    private static readonly string[] AnimationFrames = ["⠂", "⠐"];

    /// <summary>静态前缀（基准 REPL.tsx:474 TITLE_STATIC_PREFIX = '✳'）。</summary>
    private const string StaticPrefix = "✳";

    /// <summary>帧间隔 ms（基准 REPL.tsx:475 TITLE_ANIMATION_INTERVAL_MS = 960）。</summary>
    private const int FrameIntervalMs = 960;

    private readonly string _title;
    private readonly Action<string>? _setTitle;
    private readonly bool _disabled;

    private TerminalSessionStatus _status = TerminalSessionStatus.Idle;
    private bool _focused = true;
    private long _animElapsedMs;
    private string? _lastWritten;

    /// <summary>
    /// 构造标题追踪器。
    /// </summary>
    /// <param name="title">标题主体（基准 terminalTitle：会话名/agent 名/默认产品名）。</param>
    /// <param name="setTitle">标题写入委托（null = 能力降级零写）；Clear 语义 = 写空串（基准 CLEAR_TERMINAL_TITLE OSC 0 空）。</param>
    /// <param name="disabled">禁用开关（基准 CLAUDE_CODE_DISABLE_TERMINAL_TITLE — true = 全程零写）。</param>
    public TerminalTitleTracker(string title, Action<string>? setTitle, bool disabled = false)
    {
        _title = title;
        _setTitle = setTitle;
        _disabled = disabled;
    }

    /// <summary>会话三态变更（waiting &gt; busy &gt; idle — 调用方负责优先级归并）。
    /// 首次调用必写（基准 useEffect 挂载首渲染语义 — 即使状态等值）。</summary>
    /// <param name="status">目标态。</param>
    public void SetStatus(TerminalSessionStatus status)
    {
        if (_status == status && _lastWritten is not null)
        {
            return;
        }

        _status = status;
        WriteTitle();
    }

    /// <summary>
    /// 焦点变更（DEC 1004 CSI I/O 驱动；ReadKey 通道 unknown 视同 focused — 基准语义）。
    /// 失焦暂停动画：帧冻结；恢复后继续累计。非动画态焦点变化不触发写。
    /// </summary>
    /// <param name="focused">true = 聚焦/未知（不暂停）；false = 失焦。</param>
    public void SetFocused(bool focused)
    {
        if (_focused == focused)
        {
            return;
        }

        _focused = focused;
        if (_status == TerminalSessionStatus.Busy)
        {
            WriteTitle();
        }
    }

    /// <summary>
    /// 动画时钟推进（ReplShell 主循环每拍调用 — 失焦期间不累计）。
    /// 帧索引 = 累计动画时间 / 960ms % 帧数；帧变化才写。
    /// </summary>
    /// <param name="deltaMs">本拍间隔（主循环 50ms）。</param>
    public void Tick(int deltaMs)
    {
        if (_status != TerminalSessionStatus.Busy || !_focused)
        {
            return;
        }

        var before = FrameIndex;
        _animElapsedMs += deltaMs;
        if (FrameIndex != before)
        {
            WriteTitle();
        }
    }

    /// <summary>退出还原 — 写空标题（基准 CLEAR_TERMINAL_TITLE；Windows 宿主可自行还原 Console.Title 快照）。</summary>
    public void Clear()
    {
        if (_disabled || _setTitle is null)
        {
            return;
        }

        _setTitle(string.Empty);
        _lastWritten = string.Empty;
    }

    private int FrameIndex => (int)(_animElapsedMs / FrameIntervalMs) % AnimationFrames.Length;

    /// <summary>当前前缀：busy + 聚焦 = 动画帧；其余（idle/waiting/失焦冻结后首写）= 静态 ✳。</summary>
    /// <remarks>失焦冻结语义：帧保持最后值（基准 clearInterval 不换帧，标题维持）。</remarks>
    private string CurrentPrefix => _status == TerminalSessionStatus.Busy
        ? AnimationFrames[FrameIndex]
        : StaticPrefix;

    private void WriteTitle()
    {
        if (_disabled || _setTitle is null)
        {
            return;
        }

        var composed = $"{CurrentPrefix} {_title}";
        if (composed == _lastWritten)
        {
            return;
        }

        _setTitle(composed);
        _lastWritten = composed;
    }
}
