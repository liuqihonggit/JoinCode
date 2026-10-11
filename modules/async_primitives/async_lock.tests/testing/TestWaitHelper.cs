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
}
