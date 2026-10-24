namespace Core.Bridge;

/// <summary>
/// 容量唤醒信号 — TaskCompletionSource 轮换实现的异步等待/唤醒机制
/// （TASK071 #10 — 对齐 TS capacityWake.ts:28-49 controller 轮换语义：
/// WakeUp 只唤醒在途等待者并换新信号代；无等待者时信号丢弃 — 根治旧 SemaphoreSlim 实现的
/// 残留令牌陈旧唤醒 / 多等待者单放行两处语义错位）
/// 用于 Bridge 容量管理：当有容量可用时唤醒等待的工作项
/// </summary>
public sealed class CapacityWakeSignal : IDisposable {
    private volatile TaskCompletionSource<bool> _wakeSignal = CreateSignal();

    private static TaskCompletionSource<bool> CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 唤醒在途等待者 — 对齐 TS wake()：abort 当前代并换新（无等待者时信号随旧代丢弃）
    /// </summary>
    public void WakeUp() {
        var current = _wakeSignal;
        _wakeSignal = CreateSignal();
        current.TrySetResult(true);
    }

    /// <summary>
    /// 等待容量唤醒 — 快照当前代信号，唤醒或超时返回
    /// </summary>
    /// <param name="timeout">等待超时时间</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>被唤醒返回 true，超时或取消返回 false</returns>
    public async Task<bool> SleepUntilCapacityWakesAsync(TimeSpan timeout, CancellationToken ct = default) {
        var signal = _wakeSignal; // 快照当前代 — WakeUp 换新后旧代结果不影响新等待者
        try {
            var completed = await Task.WhenAny(signal.Task, Task.Delay(timeout, ct)).ConfigureAwait(false);
            return completed == signal.Task;
        } catch (OperationCanceledException) {
            return false;
        }
    }

    /// <summary>
    /// 释放信号资源 — 取消当前代（在途等待者以超时语义返回 false）
    /// </summary>
    public void Dispose() => _wakeSignal.TrySetCanceled();
}
