namespace Core.Utils;

internal interface IFireAndForgetTask : IAsyncDisposable {
    /// <summary>底层 Task 引用 — 供 Tracker WhenAll 等待</summary>
    Task Task { get; }

    /// <summary>任务是否已完成</summary>
    bool IsCompleted { get; }

    /// <summary>取消任务（不等待）— 供 Tracker 批量 Cancel 后统一 WhenAll</summary>
    void Cancel();

    /// <summary>梯度释放 — 有限等待窗口，超时放弃</summary>
    ValueTask DisposeAsync(TimeSpan gracePeriod);
}

public sealed class FireAndForgetTask<T> : IFireAndForgetTask, IAsyncDisposable {
    private readonly Task<T> _task;
    private readonly CancellationTokenSource? _cts;
    private readonly string? _name;
    private readonly Action<Exception>? _onError;
    private int _disposed;

    private FireAndForgetTask(Func<CancellationToken, Task<T>> factory, string? name, Action<Exception>? onError) {
        _name = name;
        _onError = onError;
        _cts = new CancellationTokenSource();
        _task = System.Threading.Tasks.Task.Run(() => factory(_cts.Token), _cts.Token);
    }

    private FireAndForgetTask(Task<T> task, string? name, Action<Exception>? onError) {
        _task = task;
        _name = name;
        _onError = onError;
        _cts = null;
    }

    /// <summary>从已存在的 Task 创建包装（不启动新任务，无独立 CTS，Cancel 无操作）— 用于异步初始化等需立即开始执行的场景</summary>
    public static FireAndForgetTask<Unit> FromExistingTask(Task task, string? name = null, Action<Exception>? onError = null) {
        var unitTask = task.ContinueWith(_ => Unit.Value, TaskContinuationOptions.ExecuteSynchronously);
        return new FireAndForgetTask<Unit>(unitTask, name, onError);
    }

    /// <summary>启动 fire-and-forget 任务（有返回值）</summary>
    /// <param name="factory">异步工厂委托，接收 CancellationToken</param>
    /// <param name="name">任务名称（用于诊断日志）</param>
    /// <param name="onError">异常回调（默认 null = 静默吞异常）</param>
    public static FireAndForgetTask<T> Run(Func<CancellationToken, Task<T>> factory, string? name = null, Action<Exception>? onError = null) {
        ArgumentNullException.ThrowIfNull(factory);
        return new FireAndForgetTask<T>(factory, name, onError);
    }

    /// <summary>启动 fire-and-forget 任务（无返回值）</summary>
    /// <param name="factory">异步工厂委托，接收 CancellationToken</param>
    /// <param name="name">任务名称（用于诊断日志）</param>
    /// <param name="onError">异常回调（默认 null = 静默吞异常）</param>
    public static FireAndForgetTask<Unit> Run(Func<CancellationToken, Task> factory, string? name = null, Action<Exception>? onError = null) {
        ArgumentNullException.ThrowIfNull(factory);
        return new FireAndForgetTask<Unit>(async ct => {
            await factory(ct).ConfigureAwait(false);
            return Unit.Value;
        }, name, onError);
    }

    /// <summary>底层 Task — 消费方可选 await 等待完成（强度 1：可选等待）</summary>
    public Task<T> Task => _task;

    /// <summary>任务是否已完成</summary>
    public bool IsCompleted => _task.IsCompleted;

    /// <summary>任务名称（用于诊断日志）</summary>
    public string? Name => _name;

    Task IFireAndForgetTask.Task => _task;

    void IFireAndForgetTask.Cancel() {
        if (Volatile.Read(ref _disposed) == 0 && _cts is not null) {
            try { _cts.Cancel(); } catch (ObjectDisposedException) { AsyncStderrWriter.Enqueue($"[FireAndForgetTask:{_name}] CTS 已释放，Cancel 幂等忽略"); }
        }
    }

    /// <summary>正常释放 — Cancel + 等待完成（强度 2：释放时等待）</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts?.Cancel();
        try {
            await _task.ConfigureAwait(false);
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _onError?.Invoke(ex);
        }
        _cts?.Dispose();
    }

    /// <summary>梯度释放 — Cancel + 有限等待窗口，超时放弃等待（强度 2+：紧急释放）</summary>
    /// <param name="gracePeriod">等待窗口；Zero = 不等待，Infinite = 无限等</param>
    public async ValueTask DisposeAsync(TimeSpan gracePeriod) {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts?.Cancel();
        if (gracePeriod > TimeSpan.Zero) {
            var winner = await System.Threading.Tasks.Task.WhenAny(
                _task,
                System.Threading.Tasks.Task.Delay(gracePeriod)
            ).ConfigureAwait(false);
            if (!_task.IsCompleted) {
                AsyncStderrWriter.Enqueue($"[FireAndForgetTask:{_name}] 在 {gracePeriod} 内未完成，放弃等待");
            }
        }
        if (_task.IsCompleted) {
            try {
                await _task.ConfigureAwait(false);
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                _onError?.Invoke(ex);
            }
        }
        _cts?.Dispose();
    }
}
