
namespace Core.Goal;

/// <summary>
/// 目标心跳命令 — Actor 消息类型
/// </summary>
public interface IGoalHeartbeatCommand;

public sealed record StartActivityCmd(SessionActivityReason Reason, TaskCompletionSource Tcs) : IGoalHeartbeatCommand;
public sealed record StopActivityCmd(SessionActivityReason Reason, TaskCompletionSource Tcs) : IGoalHeartbeatCommand;
public sealed record ResetHeartbeatCmd(TaskCompletionSource Tcs) : IGoalHeartbeatCommand;
public sealed record RegisterCallbackCmd(Func<CancellationToken, ValueTask> Callback) : IGoalHeartbeatCommand;
public sealed record HeartbeatTickCmd : IGoalHeartbeatCommand;

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

    public int RefCount => Volatile.Read(ref _refcount);
    public bool IsActive => Volatile.Read(ref _refcount) > 0;
    public DateTime? LastActivityAt => Volatile.Read(ref _lastActivityTicks) is { } ticks && ticks != 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
    public TimeSpan? IdleDuration => LastActivityAt.HasValue ? _clock.GetUtcNow() - LastActivityAt.Value : null;

    public GoalHeartbeat(TimeSpan? heartbeatInterval = null, ILogger<GoalHeartbeat>? logger = null, IClockService? clock = null)
        : base()
    {
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _heartbeatTimer = new Timer(_ => TrySend(new HeartbeatTickCmd()), null, Timeout.Infinite, Timeout.Infinite);
    }

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void RegisterCallback(Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        TrySend(new RegisterCallbackCmd(callback));
    }

    public async Task StartActivityAsync(SessionActivityReason reason)
    {
        var tcs = CreateTcs();
        await SendAsync(new StartActivityCmd(reason, tcs)).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task StopActivityAsync(SessionActivityReason reason)
    {
        var tcs = CreateTcs();
        await SendAsync(new StopActivityCmd(reason, tcs)).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task ResetAsync()
    {
        var tcs = CreateTcs();
        await SendAsync(new ResetHeartbeatCmd(tcs)).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

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

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[GoalHeartbeat] 消费者异常");
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _heartbeatTimer.Dispose();

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
