namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 串行门闩 — 单写者任务链实现的互斥获取原语，无锁原语、无监视器、无信号量。
/// <para>每次获取把自身完成源挂到链尾，等待前序门闩完成后进入临界区；释放时完成自身完成源唤醒后序。</para>
/// <para>前序异常/超时/取消不会毒化整条链：后续获取者忽略前序异常直接推进。</para>
/// </summary>
public sealed class AsyncGate {
    private TaskCompletionSource _tail = CreateCompletedGate();

    /// <summary>
    /// 异步获取门闩 — 超时抛 <see cref="TimeoutException"/>，取消抛 <see cref="OperationCanceledException"/>。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <param name="timeout">获取超时（null 表示无限等待）</param>
    /// <returns>释放器，释放时唤醒后序获取者</returns>
    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default, TimeSpan? timeout = null) {
        ct.ThrowIfCancellationRequested();

        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Interlocked.Exchange(ref _tail, mine);

        try {
            if (timeout.HasValue) {
                await previous.Task.WaitAsync(timeout.Value, ct).ConfigureAwait(false);
            } else {
                await previous.Task.WaitAsync(ct).ConfigureAwait(false);
            }
        } catch (TimeoutException) {
            mine.TrySetResult();
            throw new TimeoutException("AsyncGate 获取超时");
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            mine.TrySetResult();
            throw;
        } catch (Exception ex) {
            Diag.WriteLine($"[AsyncGate] 前序持有者异常,继续当前获取: {ex.Message}");
        }

        return new Releaser(mine);
    }

    /// <summary>
    /// 同步获取门闩 — 超时抛 <see cref="TimeoutException"/>。
    /// </summary>
    /// <param name="timeout">获取超时（null 表示无限等待）</param>
    /// <returns>释放器，释放时唤醒后序获取者</returns>
    public IDisposable Acquire(TimeSpan? timeout = null) {
        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Interlocked.Exchange(ref _tail, mine);

        bool acquired;
        try {
            acquired = timeout.HasValue
                ? previous.Task.Wait(timeout.Value)  // style-allow: sync-block — 闸门同步语义: 同步 WaitPrevious 按设计阻塞等待前序任务完成
                : previous.Task.Wait(Timeout.InfiniteTimeSpan);  // style-allow: sync-block — 闸门同步语义: 同步 WaitPrevious 按设计阻塞等待前序任务完成
        } catch (AggregateException ex) {
            Diag.WriteLine($"[AsyncGate] 前序持有者异常,继续当前获取: {ex.InnerException?.Message}");
            acquired = true;
        }

        if (!acquired) {
            mine.TrySetResult();
            throw new TimeoutException("AsyncGate 获取超时");
        }

        return new Releaser(mine);
    }

    private static TaskCompletionSource CreateCompletedGate() {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();
        return gate;
    }

    /// <summary>
    /// 门闩释放器 — Dispose 时完成自身完成源唤醒后序获取者（幂等）
    /// </summary>
    private sealed class Releaser : IDisposable {
        private TaskCompletionSource? _gate;

        /// <summary>
        /// 构造释放器
        /// </summary>
        /// <param name="gate">本次获取对应的完成源</param>
        public Releaser(TaskCompletionSource gate) {
            _gate = gate;
        }

        /// <summary>
        /// 释放门闩 — 仅首次调用生效
        /// </summary>
        public void Dispose() {
            var gate = Interlocked.Exchange(ref _gate, null);
            gate?.TrySetResult();
        }
    }
}
