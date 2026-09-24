
namespace Core.Hooks;

/// <summary>
/// 钩子事件广播器接口
/// </summary>
public interface IHookEventBroadcaster {
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
[Register(typeof(IHookEventBroadcaster), ServiceLifetime.Singleton)]
public sealed partial class HookEventBroadcaster : ServiceEntity, IHookEventBroadcaster {
    private ImmutableArray<Action<HookExecutionEvent>> _handlers = ImmutableArray<Action<HookExecutionEvent>>.Empty;
    private ImmutableArray<HookExecutionEvent> _pendingEvents = ImmutableArray<HookExecutionEvent>.Empty;
    private readonly ILogger<HookEventBroadcaster>? _logger;

    private const int MaxPendingEvents = 100;
    private bool _allEventsEnabled = false;

    // 始终广播的事件（低噪音的生命周期事件）
    private static readonly FrozenSet<HookEvent> AlwaysEmittedEvents = FrozenSet.Create(
        HookEvent.SessionStart,
        HookEvent.Setup);

    /// <summary>
    /// 构造函数 — 注入可选的日志记录器
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public HookEventBroadcaster(ILogger<HookEventBroadcaster>? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc />
    public void RegisterHandler(Action<HookExecutionEvent> handler) {
        ImmutableInterlocked.Update(ref _handlers, static (h, hd) => h.Add(hd), handler);

        // 原子取出挂起事件,交给新 handler 处理
        var pending = Interlocked.Exchange(ref _pendingEvents, ImmutableArray<HookExecutionEvent>.Empty);
        foreach (var pendingEvent in pending) {
            try {
                handler(pendingEvent);
            } catch (Exception ex) {
                _logger?.LogError(ex, "Failed to process pending hook event");
            }
        }
    }

    /// <inheritdoc />
    public void UnregisterHandler(Action<HookExecutionEvent> handler) {
        ImmutableInterlocked.Update(ref _handlers, static (h, hd) => {
            if (h.IsDefaultOrEmpty) return h;
            var builder = ImmutableArray.CreateBuilder<Action<HookExecutionEvent>>(h.Length);
            foreach (var x in h) if (x != hd) builder.Add(x);
            return builder.MoveToImmutable();
        }, handler);
    }

    /// <inheritdoc />
    public void BroadcastStarted(string hookId, string hookName, HookEvent hookEvent) {
        if (!ShouldEmit(hookEvent)) return;

        var evt = new HookStartedEvent {
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
        string? stderr = null) {
        if (!ShouldEmit(hookEvent)) return;

        var evt = new HookProgressEvent {
            HookId = hookId,
            HookName = hookName,
            HookEvent = hookEvent,
            Stdout = stdout,
            Stderr = stderr
        };

        Emit(evt);
    }

    /// <inheritdoc />
    public void BroadcastResponse(BroadcastContext context) {
        // 始终记录到调试日志
        if (!string.IsNullOrEmpty(context.Stdout) || !string.IsNullOrEmpty(context.Stderr)) {
            _logger?.LogDebug(
                "Hook {HookName} ({HookEvent}) {Outcome}:\n{Output}",
                context.HookName,
                context.HookEvent,
                context.Outcome,
                context.Stdout ?? context.Stderr ?? context.Output ?? "");
        }

        if (!ShouldEmit(context.HookEvent)) return;

        var evt = new HookResponseEvent {
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
    public void SetAllEventsEnabled(bool enabled) {
        _allEventsEnabled = enabled;
        _logger?.LogDebug("All hook events {Status}", enabled ? "enabled" : "disabled");
    }

    /// <inheritdoc />
    public void Clear() {
        Interlocked.Exchange(ref _handlers, ImmutableArray<Action<HookExecutionEvent>>.Empty);
        Interlocked.Exchange(ref _pendingEvents, ImmutableArray<HookExecutionEvent>.Empty);

        _allEventsEnabled = false;
    }

    private bool ShouldEmit(HookEvent hookEvent) {
        if (AlwaysEmittedEvents.Contains(hookEvent)) {
            return true;
        }

        return _allEventsEnabled;
    }

    private void Emit(HookExecutionEvent evt) {
        var handlers = _handlers;
        if (handlers.IsDefaultOrEmpty) {
            // 没有处理器，暂存事件(限制挂起数量,丢弃最老的)
            ImmutableInterlocked.Update(ref _pendingEvents, static (list, e) => {
                var newList = list.Add(e);
                return newList.Length > MaxPendingEvents
                    ? newList.RemoveRange(0, newList.Length - MaxPendingEvents)
                    : newList;
            }, evt);

            return;
        }

        foreach (var handler in handlers) {
            try {
                handler(evt);
            } catch (Exception ex) {
                _logger?.LogError(ex, "Hook event handler failed");
            }
        }
    }
}

/// <summary>
/// 钩子进度报告器
/// </summary>
public interface IHookProgressReporter {
    /// <summary>
    /// 报告进度
    /// </summary>
    Task ReportProgressAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 钩子进度报告器实现
/// </summary>
public sealed partial class HookProgressReporter : IHookProgressReporter, IDisposable {
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

    /// <summary>
    /// 构造函数 — 注入广播器、钩子标识、钩子名称、钩子事件、输出获取函数、采样间隔与日志记录器
    /// </summary>
    /// <param name="broadcaster">钩子事件广播器</param>
    /// <param name="hookId">钩子唯一标识</param>
    /// <param name="hookName">钩子名称</param>
    /// <param name="hookEvent">钩子事件类型</param>
    /// <param name="getOutput">获取当前 (stdout, stderr) 输出的函数</param>
    /// <param name="interval">采样间隔,默认 1 秒</param>
    /// <param name="logger">可选的日志记录器</param>
    public HookProgressReporter(
        IHookEventBroadcaster broadcaster,
        string hookId,
        string hookName,
        HookEvent hookEvent,
        Func<Task<(string Stdout, string Stderr)>> getOutput,
        TimeSpan? interval = null,
        ILogger? logger = null) {
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
    public void Start() {
        if (_disposed) return;

        _timer = new Timer(
            async _ => await ReportProgressAsync().ConfigureAwait(false),
            null,
            TimeSpan.Zero,
            _interval);
    }

    /// <inheritdoc />
    public async Task ReportProgressAsync(CancellationToken cancellationToken = default) {
        if (_disposed) return;

        try {
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
        } catch (Exception ex) {
            _logger?.LogError(ex, "Failed to report hook progress");
        }
    }

    /// <summary>
    /// 停止进度报告
    /// </summary>
    public void Stop() {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) return;

        _disposed = true;
        _timer?.Dispose();
    }
}

/// <summary>
/// 广播响应上下文 — 封装 BroadcastResponse 的9个参数
/// </summary>
public sealed record BroadcastContext {
    /// <summary>
    /// 钩子唯一标识
    /// </summary>
    public required string HookId { get; init; }
    /// <summary>
    /// 钩子名称
    /// </summary>
    public required string HookName { get; init; }
    /// <summary>
    /// 钩子事件类型
    /// </summary>
    public required HookEvent HookEvent { get; init; }
    /// <summary>
    /// 可选的聚合输出
    /// </summary>
    public string? Output { get; init; }
    /// <summary>
    /// 标准输出
    /// </summary>
    public string? Stdout { get; init; }
    /// <summary>
    /// 标准错误
    /// </summary>
    public string? Stderr { get; init; }
    /// <summary>
    /// 退出码
    /// </summary>
    public int? ExitCode { get; init; }
    /// <summary>
    /// 执行结果
    /// </summary>
    public required HookExecutionOutcome Outcome { get; init; }
    /// <summary>
    /// 执行耗时
    /// </summary>
    public required TimeSpan Duration { get; init; }
}