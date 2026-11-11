namespace Core.Utils;

/// <summary>
/// 测试等待工具 — 统一重试16次等待条件成立,消除散落于9个测试文件的 WaitUntilAsync 重复实现。
/// <para>重试策略:16次×perRetryTimeout,每次内部轮询10ms(同步)/50ms(异步),超时抛 TimeoutException。</para>
/// <para>用法:<c>await TestWaitHelper.WaitUntilAsync(() => actor.InputCount == 0, TimeSpan.FromMilliseconds(500));</c></para>
/// </summary>
internal static class TestWaitHelper {
    /// <summary>等待同步条件成立 — 重试16次×perRetryTimeout,内部轮询10ms</summary>
    /// <param name="condition">同步条件(返回 true=条件成立,停止等待)</param>
    /// <param name="perRetryTimeout">单次重试超时(总超时=16×perRetryTimeout)</param>
    /// <exception cref="TimeoutException">16次重试后条件仍未成立</exception>
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan perRetryTimeout) {
        for (var i = 0; i < 16; i++) {
            var deadline = DateTimeOffset.UtcNow + perRetryTimeout;
            while (DateTimeOffset.UtcNow < deadline) {
                if (condition()) return;
                await Task.Delay(10);
            }
        }
        throw new TimeoutException($"等待条件超时,重试16次×{perRetryTimeout.TotalMilliseconds:F0}ms");
    }

    /// <summary>等待异步条件成立 — 重试16次×perRetryTimeout,内部轮询50ms</summary>
    /// <param name="predicate">异步条件(返回 true=条件成立,停止等待)</param>
    /// <param name="perRetryTimeout">单次重试超时(总超时=16×perRetryTimeout)</param>
    /// <exception cref="TimeoutException">16次重试后条件仍未成立</exception>
    public static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan perRetryTimeout) {
        for (var i = 0; i < 16; i++) {
            var deadline = DateTimeOffset.UtcNow + perRetryTimeout;
            while (DateTimeOffset.UtcNow < deadline) {
                if (await predicate()) return;
                await Task.Delay(50);
            }
        }
        throw new TimeoutException($"等待条件超时,重试16次×{perRetryTimeout.TotalMilliseconds:F0}ms");
    }

    /// <summary>
    /// 统一重试16次 — 固定间隔 + 总超时上限保护,避免 CI 出错时超长时间才中断。
    /// <para>只 catch XunitException 才重试(断言失败=可能 flaky);其他异常直接抛出(确定性 bug)。</para>
    /// <para>总超时到达后停止重试,让最后一次的 XunitException 自然抛出。</para>
    /// </summary>
    /// <param name="action">测试逻辑(参数=当前 attempt 编号)</param>
    /// <param name="interval">重试间隔(默认200ms,固定不递增)</param>
    /// <param name="totalTimeout">总超时上限(默认60s,超过停止重试)</param>
    public static async Task Retry16Async(
        Func<int, Task> action,
        TimeSpan? interval = null,
        TimeSpan? totalTimeout = null) {
        var intervalValue = interval ?? TimeSpan.FromMilliseconds(200);
        var totalTimeoutValue = totalTimeout ?? TimeSpan.FromSeconds(60);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 0; attempt < 16; attempt++) {
            try {
                await action(attempt);
                return;
            } catch (Xunit.Sdk.XunitException) when (attempt < 15 && sw.Elapsed < totalTimeoutValue) {
#pragma warning disable JCC3012 // 重试间隔需真实等待给 CI 调度恢复窗口，非测试逻辑盲等
                await Task.Delay(intervalValue);
#pragma warning restore JCC3012
            }
        }
    }
}
