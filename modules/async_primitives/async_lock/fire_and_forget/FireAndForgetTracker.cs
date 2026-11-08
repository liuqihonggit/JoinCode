namespace Core.Utils;

/// <summary>
/// fire-and-forget 任务容器 — 统一注册、批量 Cancel、批量释放。
/// <para>解决 <c>_ = Task.Run(...)</c> 裸跑无 Dispose 守卫的问题。</para>
/// <para>正常释放 <see cref="DisposeAsync()"/> 等所有任务完成；紧急释放 <see cref="DisposeAsync(TimeSpan)"/> 有限等待。</para>
/// </summary>
public sealed class FireAndForgetTracker : IAsyncDisposable {
    private readonly ConcurrentQueue<IFireAndForgetTask> _tasks = new();
    private int _disposed;

    /// <summary>注册 fire-and-forget 任务（有返回值）</summary>
    /// <param name="factory">异步工厂委托，接收 CancellationToken</param>
    /// <param name="name">任务名称（用于诊断日志）</param>
    /// <param name="onError">异常回调（默认 null = 静默吞异常）</param>
    public FireAndForgetTask<T> Register<T>(Func<CancellationToken, Task<T>> factory, string? name = null, Action<Exception>? onError = null) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, typeof(FireAndForgetTracker));
        ArgumentNullException.ThrowIfNull(factory);

        var fft = FireAndForgetTask<T>.Run(factory, name, onError);
        _tasks.Enqueue(fft);
        return fft;
    }

    /// <summary>注册 fire-and-forget 任务（无返回值）</summary>
    /// <param name="factory">异步工厂委托，接收 CancellationToken</param>
    /// <param name="name">任务名称（用于诊断日志）</param>
    /// <param name="onError">异常回调（默认 null = 静默吞异常）</param>
    public FireAndForgetTask<Unit> Register(Func<CancellationToken, Task> factory, string? name = null, Action<Exception>? onError = null) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, typeof(FireAndForgetTracker));
        ArgumentNullException.ThrowIfNull(factory);

        var fft = FireAndForgetTask<Unit>.Run(factory, name, onError);
        _tasks.Enqueue(fft);
        return fft;
    }

    /// <summary>已注册任务数</summary>
    public int Count => _tasks.Count;

    /// <summary>正常释放 — Cancel 所有任务 + WhenAll 等待完成 + 逐个 Dispose CTS</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        var snapshot = _tasks.ToArray();
        _tasks.Clear();

        foreach (var fft in snapshot) {
            fft.Cancel();
        }

        if (snapshot.Length > 0) {
            try {
                await System.Threading.Tasks.Task.WhenAll(
                    snapshot.Select(t => t.Task)
                ).ConfigureAwait(false);
            } catch (Exception) {
                AsyncStderrWriter.Enqueue("[FireAndForgetTracker] WhenAll 异常忽略（fire-and-forget 语义）");
            }
        }

        foreach (var fft in snapshot) {
            await fft.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>紧急释放 — Cancel 所有任务 + 有限等待窗口 + 超时放弃</summary>
    /// <param name="gracePeriod">等待窗口；Zero = 不等待</param>
    public async ValueTask DisposeAsync(TimeSpan gracePeriod) {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        var snapshot = _tasks.ToArray();
        _tasks.Clear();

        foreach (var fft in snapshot) {
            fft.Cancel();
        }

        if (snapshot.Length > 0 && gracePeriod > TimeSpan.Zero) {
            var whenAll = System.Threading.Tasks.Task.WhenAll(snapshot.Select(t => t.Task));
            await System.Threading.Tasks.Task.WhenAny(
                whenAll,
                System.Threading.Tasks.Task.Delay(gracePeriod)
            ).ConfigureAwait(false);

            if (!whenAll.IsCompleted) {
                AsyncStderrWriter.Enqueue($"[FireAndForgetTracker] {snapshot.Length} 个任务在 {gracePeriod} 内未全部完成，放弃等待");
            }
        }

        foreach (var fft in snapshot) {
            await fft.DisposeAsync(TimeSpan.Zero).ConfigureAwait(false);
        }
    }
}
