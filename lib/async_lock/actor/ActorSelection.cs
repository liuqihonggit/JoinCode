namespace Core.Utils;

/// <summary>
/// Actor 路径选择 — 通过路径字符串寻址 Actor,发消息/查询身份(Akka ActorSelection 对齐)。
/// <para>解耦调用方与具体 Actor 类型:调用方只需知道路径,不需持有 ActorRef。</para>
/// <para>路径格式:"actorId"(顶层) — 未来扩展 "parent/child" 层级寻址。</para>
/// </summary>
public sealed class ActorSelection {
    private readonly ActorSystem _system;
    private readonly string _path;

    internal ActorSelection(ActorSystem system, string path) {
        _system = system;
        _path = path;
    }

    /// <summary>
    /// 解析路径 — 返回注册的 Actor 实例,null 表示路径不存在。
    /// </summary>
    /// <returns>Actor 实例或 null</returns>
    public IAsyncDisposable? Resolve() => _system.Selection(_path);

    /// <summary>
    /// 发消息给选中的 Actor — 类型安全泛型版,Actor 未注册或类型不匹配时静默忽略。
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <param name="msg">消息</param>
    /// <param name="sender">发送者引用(null=无 sender)</param>
    public void Tell<TCommand>(TCommand msg, object? sender = null) {
        if (_system.Selection(_path) is IActor<TCommand> actor) {
            actor.Tell(msg, sender);
        }
    }

    /// <summary>
    /// 查询选中的 Actor 身份 — 发 Identify 消息,等待 ActorIdentity 回复。
    /// <para>Actor 不存在时返回 ActorIdentity(correlationId, null)。</para>
    /// </summary>
    /// <typeparam name="TCommand">命令类型(需包含 Identify 消息)</typeparam>
    /// <param name="identifyMsg">Identify 消息</param>
    /// <param name="timeout">超时</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>ActorIdentity 回复(Subject=null 表示不存在)</returns>
    public async Task<ActorIdentity> IdentifyAsync<TCommand>(
        TCommand identifyMsg,
        TimeSpan timeout,
        CancellationToken ct = default) {
        if (_system.Selection(_path) is not IActor<TCommand> actor)
            return new ActorIdentity((identifyMsg as IIdentify)?.CorrelationId, null);
        return await ActorIdentify.IdentifyAsync(actor, identifyMsg, timeout, ct).ConfigureAwait(false);
    }
}
