
namespace Core.Goal;

/// <summary>
/// 目标心跳命令 — Actor 消息类型
/// </summary>
public interface IGoalHeartbeatCommand;

/// <summary>
/// 启动活动命令 — 携带活动原因与完成信号
/// </summary>
/// <param name="Reason">活动原因</param>
/// <param name="Tcs">完成信号源</param>
public sealed record StartActivityCmd(SessionActivityReason Reason, TaskCompletionSource Tcs) : IGoalHeartbeatCommand;

/// <summary>
/// 停止活动命令 — 携带活动原因与完成信号
/// </summary>
/// <param name="Reason">活动原因</param>
/// <param name="Tcs">完成信号源</param>
public sealed record StopActivityCmd(SessionActivityReason Reason, TaskCompletionSource Tcs) : IGoalHeartbeatCommand;

/// <summary>
/// 重置心跳命令 — 携带完成信号
/// </summary>
/// <param name="Tcs">完成信号源</param>
public sealed record ResetHeartbeatCmd(TaskCompletionSource Tcs) : IGoalHeartbeatCommand;

/// <summary>
/// 注册回调命令 — 携带心跳回调委托
/// </summary>
/// <param name="Callback">心跳回调委托</param>
public sealed record RegisterCallbackCmd(Func<CancellationToken, ValueTask> Callback) : IGoalHeartbeatCommand;

/// <summary>
/// 心跳滴答命令 — 定时器触发时发送
/// </summary>
public sealed record HeartbeatTickCmd : IGoalHeartbeatCommand;

/// <summary>
/// 目标心跳 — Actor 模型实现，管理活动引用计数与定时回调
/// </summary>
public sealed partial class GoalHeartbeat : ActorBase<IGoalHeartbeatCommand, Unit>, IGoalHeartbeat
{
    private int _disposed;
    private readonly Timer _heartbeatTimer;
    private readonly TimeSpan _heartbeatInterval;
    private readonly ILogger<GoalHeartbeat>? _logger;
    private readonly IClockService _clock;

    private int _refcount;
    private readonly Dictionary<SessionActivityReason, int> _activeReasons = new();
    private Func<CancellationToken, ValueTask>? _heartbeatCallback;
    private long _lastActivityTicks;
    private bool _timerActive;

    /// <summary>当前活动引用计数</summary>
    public int RefCount => Volatile.Read(ref _refcount);
    /// <summary>是否有活动进行中（引用计数 > 0）</summary>
    public bool IsActive => Volatile.Read(ref _refcount) > 0;
    /// <summary>最后一次活动时间，无活动时为 null</summary>
    public DateTime? LastActivityAt => Volatile.Read(ref _lastActivityTicks) is { } ticks && ticks != 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
    /// <summary>空闲时长，无活动时为 null</summary>
    public TimeSpan? IdleDuration => LastActivityAt.HasValue ? _clock.GetUtcNow() - LastActivityAt.Value : null;

    /// <summary>
    /// 构造 GoalHeartbeat — 注入可选心跳间隔、日志与时钟
    /// </summary>
    /// <param name="heartbeatInterval">心跳间隔，缺省 30 秒</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="clock">可选时钟服务，缺省使用系统时钟</param>
    public GoalHeartbeat(TimeSpan? heartbeatInterval = null, ILogger<GoalHeartbeat>? logger = null, IClockService? clock = null)
        : base()
    {
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _heartbeatTimer = new Timer(_ => TrySend(new HeartbeatTickCmd()), null, Timeout.Infinite, Timeout.Infinite);
    }

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public void RegisterCallback(Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        TrySend(new RegisterCallbackCmd(callback));
    }

    /// <inheritdoc />
    public async Task StartActivityAsync(SessionActivityReason reason)
    {
        var tcs = CreateTcs();
        await SendAsync(new StartActivityCmd(reason, tcs)).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopActivityAsync(SessionActivityReason reason)
    {
        var tcs = CreateTcs();
        await SendAsync(new StopActivityCmd(reason, tcs)).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResetAsync()
    {
        var tcs = CreateTcs();
        await SendAsync(new ResetHeartbeatCmd(tcs)).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 处理心跳命令 — Actor 消息处理逻辑
    /// </summary>
    /// <param name="command">心跳命令</param>
    /// <param name="ct">取消令牌</param>
    protected override async ValueTask HandleAsync(IGoalHeartbeatCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case RegisterCallbackCmd reg:
                _heartbeatCallback = reg.Callback;
                break;

            case StartActivityCmd start:
                _refcount++;
                _activeReasons[start.Reason] = _activeReasons.GetValueOrDefault(start.Reason) + 1;
                Volatile.Write(ref _lastActivityTicks, _clock.GetUtcNow().Ticks);

                if (_refcount == 1 && !_timerActive)
                {
                    _timerActive = true;
                    _heartbeatTimer.Change(_heartbeatInterval, _heartbeatInterval);
                }

                _logger?.LogDebug(L.T(StringKey.GoalHeartbeatActivityStarted), start.Reason, _refcount);
                start.Tcs.TrySetResult();
                break;

            case StopActivityCmd stop:
                if (_refcount > 0) _refcount--;

                if (_activeReasons.GetValueOrDefault(stop.Reason) > 0)
                {
                    _activeReasons[stop.Reason]--;
                }

                if (_refcount == 0 && _timerActive)
                {
                    _timerActive = false;
                    _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    Volatile.Write(ref _lastActivityTicks, _clock.GetUtcNow().Ticks);
                }

                _logger?.LogDebug(L.T(StringKey.GoalHeartbeatActivityStopped), stop.Reason, _refcount);
                stop.Tcs.TrySetResult();
                break;

            case ResetHeartbeatCmd reset:
                _timerActive = false;
                _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _refcount = 0;
                _activeReasons.Clear();
                Volatile.Write(ref _lastActivityTicks, 0);

                _logger?.LogDebug(L.T(StringKey.GoalHeartbeatReset));
                reset.Tcs.TrySetResult();
                break;

            case HeartbeatTickCmd:
                {
                    var callback = _heartbeatCallback;
                    if (callback != null)
                    {
                        try
                        {
                            await callback(ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, L.T(StringKey.GoalHeartbeatCallbackFailed));
                        }
                    }
                }
                break;
        }
    }

    /// <summary>
    /// 消费者异常处理 — 记录错误日志
    /// </summary>
    /// <param name="ex">异常对象</param>
    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[GoalHeartbeat] 消费者异常");
    }

    /// <summary>
    /// 异步释放 — 停止并释放心跳定时器，再释放基类资源
    /// </summary>
    public override ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _heartbeatTimer.Dispose();

        _ = base.DisposeAsync();
        return ValueTask.CompletedTask;
    }
}
