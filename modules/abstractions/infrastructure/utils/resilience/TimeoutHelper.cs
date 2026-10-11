namespace Core.Utils;

/// <summary>
/// 超时辅助工具 — 提供链接取消令牌与带超时的异步操作包装
/// </summary>
public static class TimeoutHelper {
    /// <summary>
    /// 创建链接到指定令牌并在超时后自动取消的 CancellationTokenSource
    /// </summary>
    /// <param name="ct">外部取消令牌</param>
    /// <param name="timeout">超时时长</param>
    /// <returns>链接后的 CancellationTokenSource,调用方负责释放</returns>
    public static CancellationTokenSource CreateLinkedTimeout(CancellationToken ct, TimeSpan timeout) {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return cts;
    }

    /// <summary>
    /// 执行带超时的异步操作,超时抛出 TimeoutException
    /// </summary>
    /// <param name="operation">异步操作,接收链接后的取消令牌</param>
    /// <param name="timeout">超时时长</param>
    /// <param name="ct">外部取消令牌</param>
    public static async Task WithTimeoutAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        CancellationToken ct = default) {
        using var cts = CreateLinkedTimeout(ct, timeout);
        try {
            await operation(cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new TimeoutException($"[INF042] 操作在 {timeout.TotalMilliseconds}ms 内未完成");
        }
    }

    /// <summary>
    /// 执行带超时的异步操作并返回结果,超时抛出 TimeoutException
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="operation">异步操作,接收链接后的取消令牌</param>
    /// <param name="timeout">超时时长</param>
    /// <param name="ct">外部取消令牌</param>
    /// <returns>操作返回值</returns>
    public static async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken ct = default) {
        using var cts = CreateLinkedTimeout(ct, timeout);
        try {
            return await operation(cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new TimeoutException($"[INF043] 操作在 {timeout.TotalMilliseconds}ms 内未完成");
        }
    }
}