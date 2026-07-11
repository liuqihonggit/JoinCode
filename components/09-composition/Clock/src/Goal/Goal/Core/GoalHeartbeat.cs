
namespace Core.Goal;

public sealed partial class GoalHeartbeat : IGoalHeartbeat
{
    private int _refcount;
    private readonly Dictionary<SessionActivityReason, int> _activeReasons = new();
    private PeriodicTimer? _heartbeatTimer;
    private Func<CancellationToken, ValueTask>? _heartbeatCallback;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatLoop;
    private readonly TimeSpan _heartbeatInterval;
    private readonly SemaphoreSlim _stateLock;
    [Inject] private readonly ILogger<GoalHeartbeat>? _logger;
    [Inject] private readonly IClockService _clock;

    public int RefCount => Volatile.Read(ref _refcount);
    public bool IsActive => Volatile.Read(ref _refcount) > 0;
    public DateTime? LastActivityAt { get; private set; }
    public TimeSpan? IdleDuration => LastActivityAt.HasValue ? _clock.GetUtcNow() - LastActivityAt.Value : null;

    public GoalHeartbeat( TimeSpan? heartbeatInterval = null, ILogger<GoalHeartbeat>? logger = null, IClockService? clock = null)
    {
        _stateLock = new SemaphoreSlim(1, 1);
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    public void RegisterCallback(Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _heartbeatCallback = callback;
    }

    public async Task StartActivityAsync(SessionActivityReason reason)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _refcount++;
            _activeReasons[reason] = _activeReasons.GetValueOrDefault(reason) + 1;
            LastActivityAt = _clock.GetUtcNow();

            if (_refcount == 1)
            {
                StartHeartbeatTimer();
            }
        }
        finally
        {
            _stateLock.Release();
        }

        _logger?.LogDebug(L.T(StringKey.GoalHeartbeatActivityStarted), reason, _refcount);
    }

    public async Task StopActivityAsync(SessionActivityReason reason)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_refcount > 0) _refcount--;

            if (_activeReasons.GetValueOrDefault(reason) > 0)
            {
                _activeReasons[reason]--;
            }

            if (_refcount == 0 && _heartbeatTimer != null)
            {
                StopHeartbeatTimer();
                LastActivityAt = _clock.GetUtcNow();
            }
        }
        finally
        {
            _stateLock.Release();
        }

        _logger?.LogDebug(L.T(StringKey.GoalHeartbeatActivityStopped), reason, _refcount);
    }

    public async Task ResetAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            StopHeartbeatTimer();
            _refcount = 0;
            _activeReasons.Clear();
            LastActivityAt = null;
        }
        finally
        {
            _stateLock.Release();
        }

        _logger?.LogDebug(L.T(StringKey.GoalHeartbeatReset));
    }

    private void StartHeartbeatTimer()
    {
        _cts = new CancellationTokenSource();
        _heartbeatTimer = new PeriodicTimer(_heartbeatInterval);
        _heartbeatLoop = Task.Run(() => RunHeartbeatLoopAsync(_cts.Token));
    }

    private void StopHeartbeatTimer()
    {
        _cts?.Cancel();
        // 不在此处 Dispose timer，避免 RunHeartbeatLoopAsync 中 WaitForNextTickAsync
        // 抛出 InvalidOperationException。timer 将在 DisposeAsync 中等待循环结束后清理。
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var timer = _heartbeatTimer;
                if (timer == null) break;

                bool ticked;
                try
                {
                    ticked = await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                if (!ticked) break;

                var callback = _heartbeatCallback;
                if (callback != null)
                {
                    try
                    {
                        await callback(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, L.T(StringKey.GoalHeartbeatCallbackFailed));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (NullReferenceException ex)
        {
            _logger?.LogError(ex, "GoalHeartbeat loop NullReference");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _cts?.Cancel();
        }
        finally
        {
            _stateLock.Release();
        }

        if (_heartbeatLoop != null)
        {
            try
            {
#pragma warning disable VSTHRD003
                await _heartbeatLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
            }
            catch (NullReferenceException ex)
            {
                _logger?.LogError(ex, "GoalHeartbeat.DisposeAsync NullReference");
            }
        }

        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _cts?.Dispose();
        _cts = null;

        _refcount = 0;
        _activeReasons.Clear();
        _stateLock.Dispose();
    }
}
