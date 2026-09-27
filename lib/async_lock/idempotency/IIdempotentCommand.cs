namespace Core.Utils;

/// <summary>
/// 幂等命令 — 携带幂等键的命令，接收方守卫层用此键做去重。
/// <para>与 <see cref="IIdempotent"/> 区别：<see cref="IIdempotent"/> 是标记接口(命令本身幂等,重试安全)；</para>
/// <para>本接口携带具体幂等键,用于键控去重+缓存结果。</para>
/// <para>一个命令可同时实现两者:既标记幂等,又携带键控去重键。</para>
/// </summary>
public interface IIdempotentCommand {
    /// <summary>幂等键 — 业务流水号+操作标识，重试不变</summary>
    IdempotencyKey IdempotencyKey { get; }
}

/// <summary>
/// 请求命令 — 双 Tell 协议的请求端，携带幂等键 + 缓存恢复能力。
/// <para><b>双 Tell 工作流</b>:</para>
/// <para>1. 发送方 Tell 请求命令到 Actor 输入通道(SendAsync)</para>
/// <para>2. Consumer 收到命令,先检查幂等缓存:</para>
/// <para>   命中 → 调用 <see cref="TryRestoreFromCache"/> 恢复结果(Tell 回执) → 跳过 HandleAsync</para>
/// <para>   未命中 → 执行 HandleAsync → 派生类自行 TryRegister 缓存结果 → Tell 回执</para>
/// <para>3. 发送方通过输出通道(OutputAsync)拉取回执,无需 Ask 阻塞等待</para>
/// <para><b>与 Ask 模式区别</b>:Ask 阻塞等待 TCS;双 Tell 通过输出通道异步回执,无死锁风险。</para>
/// </summary>
/// <typeparam name="TOut">回执消息类型 — 与 Actor 的 TOut 一致</typeparam>
public interface IRequestCommand<TOut> : IIdempotentCommand {
    /// <summary>
    /// 从缓存恢复结果 — 命中缓存时由 ActorBase ConsumeLoop 调用。
    /// <para>实现从 <paramref name="store"/> 取回缓存结果,通过 <paramref name="publish"/> 委托发布回执(Tell)。</para>
    /// </summary>
    /// <param name="store">幂等去重存储</param>
    /// <param name="publish">回执发布委托 — 传入缓存结果,由 ActorBase.TryPublish 写入输出通道</param>
    /// <returns>true=命中缓存并已发布回执(跳过 HandleAsync);false=未命中(需执行 HandleAsync)</returns>
    bool TryRestoreFromCache(IIdempotencyStore store, Action<TOut> publish);
}
