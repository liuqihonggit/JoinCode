namespace Core.Utils;

/// <summary>
/// 优雅停止工具 — 发 PoisonPill 消息后等待 Actor 终止(Akka 对齐)。
/// <para>语义:发 PoisonPill 排入邮箱尾部,前面的消息处理完后 Actor 自动停止,等待终止完成。</para>
/// <para>超时抛 <see cref="TimeoutException"/>,调用方可重试或强制 DisposeAsync。</para>
/// </summary>
public static class ActorGracefulStop {
    /// <summary>
    /// 优雅停止 Actor — 发 PoisonPill 消息后等待 Actor 终止。
    /// <para>PoisonPill 排入邮箱 FIFO,前面的消息处理完后 Actor 自动停止。</para>
    /// </summary>
    /// <typeparam name="TCommand">命令类型(需包含 PoisonPill 消息)</typeparam>
    /// <param name="actor">目标 Actor</param>
    /// <param name="poisonPill">PoisonPill 消息(实现 <see cref="IPoisonPill"/>)</param>
    /// <param name="timeout">超时(超时抛 <see cref="TimeoutException"/>)</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="TimeoutException">超时未终止</exception>
    public static async Task GracefulStopAsync<TCommand>(
        IActor<TCommand> actor,
        TCommand poisonPill,
        TimeSpan timeout,
        CancellationToken ct = default) {
        actor.Tell(poisonPill);
        await WaitUntilDisposedAsync(actor, timeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 优雅停止 Actor — 发 PoisonPill 后等待终止,无 PoisonPill 消息时直接等 Dispose。
    /// <para>Actor 未实现 <see cref="IActorLifecycle"/> 时抛 <see cref="ArgumentException"/>。</para>
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <param name="actor">目标 Actor</param>
    /// <param name="poisonPill">PoisonPill 消息</param>
    /// <param name="timeoutMs">超时毫秒(默认10s)</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="TimeoutException">超时未终止</exception>
    public static async Task GracefulStopAsync<TCommand>(
        IActor<TCommand> actor,
        TCommand poisonPill,
        int timeoutMs = 10_000,
        CancellationToken ct = default)
        => await GracefulStopAsync(actor, poisonPill, TimeSpan.FromMilliseconds(timeoutMs), ct).ConfigureAwait(false);

    private static async Task WaitUntilDisposedAsync(object actor, TimeSpan timeout, CancellationToken ct) {
        if (actor is not IActorLifecycle lifecycle)
            throw new ArgumentException($"Actor 必须实现 {nameof(IActorLifecycle)}", nameof(actor));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try {
            while (!lifecycle.IsDisposed) {
                await Task.Delay(10, cts.Token).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new TimeoutException($"GracefulStop 超时 {timeout.TotalMilliseconds:F0}ms,Actor 未终止");
        }
    }
}
