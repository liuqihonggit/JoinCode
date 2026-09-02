namespace IO.Services;

/// <summary>
/// 主动模式 tick 调度器 — 管理 tick 间隔和下一次 tick 时间
/// 对齐 TS 原版 proactive/index.ts 的 nextTickAt 逻辑
/// </summary>
public sealed class ProactiveTickScheduler
{
    private readonly IProactiveStateService _stateService;
    private readonly TerminalFocusDetector _focusDetector;
    private readonly ILogger<ProactiveTickScheduler>? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _tickInterval;
    private readonly TimeSpan _blurredTickInterval;
    private DateTimeOffset? _nextTickAt;
    private long _tickCount;

    /// <summary>
    /// 创建 ProactiveTickScheduler
    /// </summary>
    /// <param name="stateService">主动模式状态服务</param>
    /// <param name="focusDetector">终端焦点检测器</param>
    /// <param name="logger">日志器</param>
    /// <param name="tickInterval">tick 间隔（终端聚焦时，默认 5s）</param>
    /// <param name="blurredTickInterval">tick 间隔（终端失焦时，默认 30s）</param>
    /// <param name="clock">时钟（测试注入，默认 DateTimeOffset.UtcNow）</param>
    public ProactiveTickScheduler(
        IProactiveStateService stateService,
        TerminalFocusDetector focusDetector,
        ILogger<ProactiveTickScheduler>? logger = null,
        TimeSpan? tickInterval = null,
        TimeSpan? blurredTickInterval = null,
        Func<DateTimeOffset>? clock = null)
    {
        _stateService = stateService;
        _focusDetector = focusDetector;
        _logger = logger;
        _tickInterval = tickInterval ?? TimeSpan.FromSeconds(5);
        _blurredTickInterval = blurredTickInterval ?? TimeSpan.FromSeconds(30);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>下一次 tick 时间（null = 未调度）</summary>
    public DateTimeOffset? NextTickAt => _nextTickAt;

    /// <summary>已发送 tick 数</summary>
    public long TickCount => Interlocked.Read(ref _tickCount);

    /// <summary>tick 间隔（终端聚焦时）</summary>
    public TimeSpan TickInterval => _tickInterval;

    /// <summary>tick 间隔（终端失焦时）</summary>
    public TimeSpan BlurredTickInterval => _blurredTickInterval;

    /// <summary>
    /// 检查是否应该发送 tick — 主动模式激活 + 未暂停 + 上下文未阻塞 + 到达 tick 时间
    /// </summary>
    public bool ShouldTick()
    {
        if (!_stateService.IsActive || _stateService.IsPaused || _stateService.IsContextBlocked)
        {
            return false;
        }

        var now = _clock();
        if (_nextTickAt is null || now >= _nextTickAt)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 生成 tick 内容并调度下一次 tick
    /// </summary>
    /// <returns>tick 内容（如 "&lt;tick&gt;14:30:05&lt;/tick&gt;"），或 null 如果不应 tick</returns>
    public string? GenerateTick()
    {
        if (!ShouldTick())
        {
            return null;
        }

        var now = _clock();
        Interlocked.Increment(ref _tickCount);

        var interval = _focusDetector.IsFocused ? _tickInterval : _blurredTickInterval;
        _nextTickAt = now + interval;

        var timeStr = now.LocalDateTime.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        _logger?.LogDebug("主动模式 tick #{Count} at {Time}, next at {Next}", _tickCount, timeStr, _nextTickAt);

        return $"<tick>{timeStr}</tick>";
    }

    /// <summary>
    /// 重置调度（激活/停用/清除上下文时调用）
    /// </summary>
    public void Reset()
    {
        _nextTickAt = null;
    }

    /// <summary>
    /// 立即调度下一次 tick（不等间隔）
    /// </summary>
    public void ScheduleImmediate()
    {
        _nextTickAt = _clock();
    }
}
