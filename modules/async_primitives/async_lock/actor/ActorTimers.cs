namespace Core.Utils;

/// <summary>
/// Actor 定时器 — 绑定到 Actor 生命周期的定时器,Actor Dispose 时自动取消所有(Akka Timers 对齐)。
/// <para>与 <see cref="IScheduler"/> 区别:Timers 绑定到 Actor 生命周期,Actor Dispose 自动取消;Scheduler 是全局的。</para>
/// <para>用 key 标识定时器,相同 key 的 Start*Timer 会先取消旧定时器再创建新的。</para>
/// </summary>
/// <typeparam name="TCommand">Actor 命令类型</typeparam>
public sealed class ActorTimers<TCommand> : IAsyncDisposable {
    private readonly IActorTell<TCommand> _actor;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>构造 Actor 定时器</summary>
    /// <param name="actor">目标 Actor(发消息目标)</param>
    public ActorTimers(IActorTell<TCommand> actor) {
        _actor = actor;
    }

    /// <summary>
    /// 启动单次定时器 — delay 后发 msg 给 Actor,相同 key 的旧定时器先取消。
    /// </summary>
    /// <param name="key">定时器标识(相同 key 先取消旧定时器)</param>
    /// <param name="msg">定时触发的消息</param>
    /// <param name="delay">延迟时间</param>
    public void StartSingleTimer(string key, TCommand msg, TimeSpan delay) {
        Cancel(key);
        if (Volatile.Read(ref _disposed) != 0) return;
        var cts = new CancellationTokenSource();
        _timers[key] = cts;
        _ = Task.Run(async () => {
            try {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                _actor.Tell(msg);
            } catch (OperationCanceledException) {
            } finally {
                _timers.TryRemove(key, out _);
                cts.Dispose();
            }
        });
    }

    /// <summary>
    /// 启动重复定时器 — 初始延迟后按 interval 重复发 msg,相同 key 的旧定时器先取消。
    /// </summary>
    /// <param name="key">定时器标识</param>
    /// <param name="msg">定时触发的消息</param>
    /// <param name="initialDelay">初始延迟</param>
    /// <param name="interval">重复间隔</param>
    public void StartPeriodicTimer(string key, TCommand msg, TimeSpan initialDelay, TimeSpan interval) {
        Cancel(key);
        if (Volatile.Read(ref _disposed) != 0) return;
        var cts = new CancellationTokenSource();
        _timers[key] = cts;
        _ = Task.Run(async () => {
            try {
                await Task.Delay(initialDelay, cts.Token).ConfigureAwait(false);
                while (!cts.IsCancellationRequested) {
                    _actor.Tell(msg);
                    await Task.Delay(interval, cts.Token).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
            } finally {
                _timers.TryRemove(key, out _);
                cts.Dispose();
            }
        });
    }

    /// <summary>取消指定 key 的定时器</summary>
    /// <param name="key">定时器标识</param>
    public void Cancel(string key) {
        if (_timers.TryRemove(key, out var cts)) {
            cts.Cancel();
        }
    }

    /// <summary>取消所有定时器</summary>
    public void CancelAll() {
        foreach (var kv in _timers) {
            if (_timers.TryRemove(kv.Key, out var cts)) {
                cts.Cancel();
            }
        }
    }

    /// <summary>当前活跃定时器数量</summary>
    public int Count => _timers.Count;

    /// <summary>释放 — 取消所有定时器</summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        CancelAll();
        return ValueTask.CompletedTask;
    }
}
