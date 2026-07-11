
namespace Core.Hooks;

/// <summary>
/// 钩子事件广播器接口
/// </summary>
public interface IHookEventBroadcaster
{
    /// <summary>
    /// 注册事件处理器
    /// </summary>
    void RegisterHandler(Action<HookExecutionEvent> handler);

    /// <summary>
    /// 注销事件处理器
    /// </summary>
    void UnregisterHandler(Action<HookExecutionEvent> handler);

    /// <summary>
    /// 广播钩子开始事件
    /// </summary>
    void BroadcastStarted(string hookId, string hookName, HookEvent hookEvent);

    /// <summary>
    /// 广播钩子进度事件
    /// </summary>
    void BroadcastProgress(
        string hookId,
        string hookName,
        HookEvent hookEvent,
        string? stdout = null,
        string? stderr = null);

    /// <summary>
    /// 广播钩子响应事件
    /// </summary>
    void BroadcastResponse(BroadcastContext context);

    /// <summary>
    /// 启用/禁用所有钩子事件
    /// </summary>
    void SetAllEventsEnabled(bool enabled);

    /// <summary>
    /// 清除状态
    /// </summary>
    void Clear();
}

/// <summary>
/// 钩子事件广播器实现
/// </summary>
[Register]
public sealed partial class HookEventBroadcaster : IHookEventBroadcaster
{
    private readonly ConcurrentBag<Action<HookExecutionEvent>> _handlers = new();
    private readonly ConcurrentQueue<HookExecutionEvent> _pendingEvents = new();
    [Inject] private readonly ILogger<HookEventBroadcaster>? _logger;

    private const int MaxPendingEvents = 100;
    private bool _allEventsEnabled = false;

    // 始终广播的事件（低噪音的生命周期事件）
    private static readonly FrozenSet<HookEvent> AlwaysEmittedEvents = FrozenSet.Create(
        HookEvent.SessionStart,
        HookEvent.Setup);

    public HookEventBroadcaster(ILogger<HookEventBroadcaster>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void RegisterHandler(Action<HookExecutionEvent> handler)
    {
        _handlers.Add(handler);

        // 处理挂起的事件
        while (_pendingEvents.TryDequeue(out var pendingEvent))
        {
            try
            {
                handler(pendingEvent);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to process pending hook event");
            }
        }
    }

    /// <inheritdoc />
    public void UnregisterHandler(Action<HookExecutionEvent> handler)
    {
        // ConcurrentBag 不支持直接移除，需要重新创建
        var newHandlers = new ConcurrentBag<Action<HookExecutionEvent>>(
            _handlers.Where(h => h != handler));

        while (!_handlers.IsEmpty)
        {
            _handlers.TryTake(out _);
        }

        foreach (var h in newHandlers)
        {
            _handlers.Add(h);
        }
    }

    /// <inheritdoc />
    public void BroadcastStarted(string hookId, string hookName, HookEvent hookEvent)
    {
        if (!ShouldEmit(hookEvent)) return;

        var evt = new HookStartedEvent
        {
            HookId = hookId,
            HookName = hookName,
            HookEvent = hookEvent
        };

        Emit(evt);
    }

    /// <inheritdoc />
    public void BroadcastProgress(
        string hookId,
        string hookName,
        HookEvent hookEvent,
        string? stdout = null,
        string? stderr = null)
    {
        if (!ShouldEmit(hookEvent)) return;

        var evt = new HookProgressEvent
        {
            HookId = hookId,
            HookName = hookName,
            HookEvent = hookEvent,
            Stdout = stdout,
            Stderr = stderr
        };

        Emit(evt);
    }

    /// <inheritdoc />
    public void BroadcastResponse(BroadcastContext context)
    {
        // 始终记录到调试日志
        if (!string.IsNullOrEmpty(context.Stdout) || !string.IsNullOrEmpty(context.Stderr))
        {
            _logger?.LogDebug(
                "Hook {HookName} ({HookEvent}) {Outcome}:\n{Output}",
                context.HookName,
                context.HookEvent,
                context.Outcome,
                context.Stdout ?? context.Stderr ?? context.Output ?? "");
        }

        if (!ShouldEmit(context.HookEvent)) return;

        var evt = new HookResponseEvent
        {
            HookId = context.HookId,
            HookName = context.HookName,
            HookEvent = context.HookEvent,
            Output = context.Output,
            Stdout = context.Stdout,
            Stderr = context.Stderr,
            ExitCode = context.ExitCode,
            Outcome = context.Outcome,
            Duration = context.Duration
        };

        Emit(evt);
    }

    /// <inheritdoc />
    public void SetAllEventsEnabled(bool enabled)
    {
        _allEventsEnabled = enabled;
        _logger?.LogDebug("All hook events {Status}", enabled ? "enabled" : "disabled");
    }

    /// <inheritdoc />
    public void Clear()
    {
        while (!_handlers.IsEmpty)
        {
            _handlers.TryTake(out _);
        }

        while (!_pendingEvents.IsEmpty)
        {
            _pendingEvents.TryDequeue(out _);
        }

        _allEventsEnabled = false;
    }

    private bool ShouldEmit(HookEvent hookEvent)
    {
        if (AlwaysEmittedEvents.Contains(hookEvent))
        {
            return true;
        }

        return _allEventsEnabled;
    }

    private void Emit(HookExecutionEvent evt)
    {
        if (_handlers.IsEmpty)
        {
            // 没有处理器，暂存事件
            _pendingEvents.Enqueue(evt);

            // 限制挂起事件数量
            while (_pendingEvents.Count > MaxPendingEvents)
            {
                _pendingEvents.TryDequeue(out _);
            }

            return;
        }

        foreach (var handler in _handlers)
        {
            try
            {
                handler(evt);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Hook event handler failed");
            }
        }
    }
}

/// <summary>
/// 钩子进度报告器
/// </summary>
public interface IHookProgressReporter
{
    /// <summary>
    /// 报告进度
    /// </summary>
    Task ReportProgressAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 钩子进度报告器实现
/// </summary>
public sealed partial class HookProgressReporter : IHookProgressReporter, IDisposable
{
    private readonly IHookEventBroadcaster _broadcaster;
    private readonly string _hookId;
    private readonly string _hookName;
    private readonly HookEvent _hookEvent;
    private readonly Func<Task<(string Stdout, string Stderr)>> _getOutput;
    private readonly TimeSpan _interval;
    private readonly ILogger? _logger;

    private Timer? _timer;
    private string _lastEmittedOutput = "";
    private bool _disposed;

    public HookProgressReporter(
        IHookEventBroadcaster broadcaster,
        string hookId,
        string hookName,
        HookEvent hookEvent,
        Func<Task<(string Stdout, string Stderr)>> getOutput,
        TimeSpan? interval = null,
        ILogger? logger = null)
    {
        _broadcaster = broadcaster;
        _hookId = hookId;
        _hookName = hookName;
        _hookEvent = hookEvent;
        _getOutput = getOutput;
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _logger = logger;
    }

    /// <summary>
    /// 开始进度报告
    /// </summary>
    public void Start()
    {
        if (_disposed) return;

        _timer = new Timer(
            async _ => await ReportProgressAsync().ConfigureAwait(false),
            null,
            TimeSpan.Zero,
            _interval);
    }

    /// <inheritdoc />
    public async Task ReportProgressAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            var (stdout, stderr) = await _getOutput().ConfigureAwait(false);
            var output = stdout + stderr;

            if (output == _lastEmittedOutput) return;

            _lastEmittedOutput = output;

            _broadcaster.BroadcastProgress(
                _hookId,
                _hookName,
                _hookEvent,
                stdout,
                stderr);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to report hook progress");
        }
    }

    /// <summary>
    /// 停止进度报告
    /// </summary>
    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _timer?.Dispose();
    }
}

/// <summary>
/// 广播响应上下文 — 封装 BroadcastResponse 的9个参数
/// </summary>
public sealed record BroadcastContext
{
    public required string HookId { get; init; }
    public required string HookName { get; init; }
    public required HookEvent HookEvent { get; init; }
    public string? Output { get; init; }
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public int? ExitCode { get; init; }
    public required HookExecutionOutcome Outcome { get; init; }
    public required TimeSpan Duration { get; init; }
}
