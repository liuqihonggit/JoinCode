namespace Core.Utils;

/// <summary>
/// Actor 识别工具 — 发 Identify 消息查询 Actor 身份,等待 ActorIdentity 回复(Akka 对齐)。
/// <para>语义:发 IdentifyMessage 给目标 Actor,Actor 自动回复 ActorIdentity(含引用)。</para>
/// <para>超时抛 <see cref="TimeoutException"/>,表示 Actor 不存在或不可达。</para>
/// </summary>
public static class ActorIdentify {
    /// <summary>
    /// 回复通道 — 包装 TaskCompletionSource,ActorBase 检测到 IIdentify 后通过此通道回复。
    /// </summary>
    internal sealed class ReplyChannel(TaskCompletionSource<ActorIdentity> tcs) {
        /// <summary>回复 ActorIdentity 给等待的 IdentifyAsync 调用方</summary>
        public void Reply(ActorIdentity identity) => tcs.TrySetResult(identity);
    }

    /// <summary>
    /// 查询 Actor 身份 — 发 Identify 消息后等待 ActorIdentity 回复。
    /// </summary>
    /// <typeparam name="TCommand">命令类型(需包含 Identify 消息)</typeparam>
    /// <param name="actor">目标 Actor</param>
    /// <param name="identifyMsg">Identify 消息(实现 <see cref="IIdentify"/>)</param>
    /// <param name="timeout">超时(超时抛 <see cref="TimeoutException"/>)</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="TimeoutException">超时未回复</exception>
    public static async Task<ActorIdentity> IdentifyAsync<TCommand>(
        IActor<TCommand> actor,
        TCommand identifyMsg,
        TimeSpan timeout,
        CancellationToken ct = default) {
        var tcs = new TaskCompletionSource<ActorIdentity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new ReplyChannel(tcs);
        actor.Tell(identifyMsg, channel);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new TimeoutException($"Identify 超时 {timeout.TotalMilliseconds:F0}ms,Actor 未回复");
        }
    }

    /// <summary>
    /// 查询 Actor 身份 — 发 Identify 消息后等待 ActorIdentity 回复。
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <param name="actor">目标 Actor</param>
    /// <param name="identifyMsg">Identify 消息</param>
    /// <param name="timeoutMs">超时毫秒(默认10s)</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="TimeoutException">超时未回复</exception>
    public static Task<ActorIdentity> IdentifyAsync<TCommand>(
        IActor<TCommand> actor,
        TCommand identifyMsg,
        int timeoutMs = 10_000,
        CancellationToken ct = default)
        => IdentifyAsync(actor, identifyMsg, TimeSpan.FromMilliseconds(timeoutMs), ct);
}
