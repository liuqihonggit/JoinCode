namespace Core.Bridge;

/// <summary>
/// 容量唤醒信号 — 基于 SemaphoreSlim 实现的异步等待/唤醒机制
/// 用于 Bridge 容量管理：当有容量可用时唤醒等待的工作项
/// </summary>
public sealed class CapacityWakeSignal : IDisposable {
    private readonly SemaphoreSlim _semaphore = new(0, int.MaxValue);
    private volatile int _wakeToken;

    /// <summary>
    /// 唤醒一个等待的工作项 — 释放信号量
    /// </summary>
    public void WakeUp() {
        Interlocked.Exchange(ref _wakeToken, Interlocked.Increment(ref _wakeToken));
        _semaphore.Release();
    }

    /// <summary>
    /// 等待容量唤醒 — 阻塞直到被唤醒或超时
    /// </summary>
    /// <param name="timeout">等待超时时间</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>被唤醒返回 true，超时或取消返回 false</returns>
    public async Task<bool> SleepUntilCapacityWakesAsync(TimeSpan timeout, CancellationToken ct = default) {
        try {
            return await _semaphore.WaitAsync(timeout, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            return false;
        }
    }

    /// <summary>
    /// 释放信号量资源
    /// </summary>
    public void Dispose() => _semaphore.Dispose();
}