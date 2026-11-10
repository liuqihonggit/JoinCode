namespace Core.Agents.Coordinator;

/// <summary>
/// 邮箱 Actor 借用句柄 — 仅暴露 Tell 发消息能力，不实现 IDisposable/IAsyncDisposable。
/// <para>调用方只借用 Actor 发消息（不拥有），所有权由 TeammateMailboxService._actors 容器持有，DisposeAsync 统一释放。</para>
/// <para>用于消除 JCC9305 误报：分析器按"IDisposable 变量即拥有"规则，把借用方当拥有方报泄露。</para>
/// </summary>
internal readonly struct MailboxActorRef {
    private readonly MailboxActor _actor;

    internal MailboxActorRef(MailboxActor actor) => _actor = actor;

    /// <summary>向邮箱 Actor 发送命令（借用，不转移所有权）</summary>
    public void Tell(MailboxCommand cmd, object? sender = null) => _actor.Tell(cmd, sender);
}
