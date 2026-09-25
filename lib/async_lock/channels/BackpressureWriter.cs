namespace JoinCode.AsyncLock.Channels;

/// <summary>
/// 背压写入器 — 封装 TryWrite + 延迟重试循环(16次+指数退避+换流水号)
/// <para>射后不理模式: 写入方不阻塞Actor消费循环,失败时后台重试</para>
/// <para>协议: 每次重试更换消息流水号(BackpressureChannel.TryWrite内部自增),重试次数计数</para>
/// </summary>
public static class BackpressureWriter {

    /// <summary>最大重试次数</summary>
    public const int MaxRetries = 16;

    /// <summary>
    /// 射后不理写入 — TryWrite,成功即返回;失败时后台重试(16次+指数退避)
    /// <para>不阻塞调用方,异常和重试失败仅日志记录</para>
    /// </summary>
    /// <typeparam name="T">业务数据类型</typeparam>
    /// <param name="channel">背压通道</param>
    /// <param name="item">业务数据</param>
    /// <param name="targetId">接收方标识</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="logger">日志器(可选,记录重试失败)</param>
    public static void WriteFireAndForget<T>(
        BackpressureChannel<T> channel,
        T item,
        string targetId,
        CancellationToken ct,
        ILogger? logger = null) where T : notnull {
        if (channel.TryWrite(item, targetId, 0)) return;
        _ = RetryLoopAsync(channel, item, targetId, ct, logger);
    }

    /// <summary>
    /// 重试写入循环 — 16次重试+指数退避+消费背压延迟信号
    /// </summary>
    /// <typeparam name="T">业务数据类型</typeparam>
    /// <param name="channel">背压通道</param>
    /// <param name="item">业务数据</param>
    /// <param name="targetId">接收方标识</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="logger">日志器(可选)</param>
    /// <returns>true=最终写入成功,false=16次重试失败</returns>
    public static async ValueTask<bool> WriteWithRetryAsync<T>(
        BackpressureChannel<T> channel,
        T item,
        string targetId,
        CancellationToken ct,
        ILogger? logger = null) where T : notnull {
        return await RetryLoopAsync(channel, item, targetId, ct, logger).ConfigureAwait(false);
    }

    private static async ValueTask<bool> RetryLoopAsync<T>(
        BackpressureChannel<T> channel,
        T item,
        string targetId,
        CancellationToken ct,
        ILogger? logger) where T : notnull {

        for (var retry = 1; retry <= MaxRetries; retry++) {
            ct.ThrowIfCancellationRequested();

            var bpDelay = channel.ConsumePendingSignals();
            if (bpDelay > TimeSpan.Zero)
                await Task.Delay(bpDelay, ct).ConfigureAwait(false);

            if (channel.TryWrite(item, targetId, retry))
                return true;

            var backoff = TimeSpan.FromMilliseconds(100 * Math.Pow(2, Math.Min(retry, 10)));
            await Task.Delay(backoff, ct).ConfigureAwait(false);
        }

        logger?.LogWarning("BackpressureChannel: {MaxRetries}次重试失败,消息未能写入: Target={Target}", MaxRetries, targetId);
        return false;
    }
}
