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
/// 请求命令 — 双 Tell 协议的请求端，携带幂等键 + 缓存恢复能力 + 背压回调。
/// <para><b>双 Tell 工作流</b>:</para>
/// <para>1. 发送方创建命令(回调有默认值,从 <see cref="ActorDefaults"/> 取) → Tell 请求命令到 Actor 输入通道</para>
/// <para>2. Consumer 收到命令,检测水位线变化 → 调用 <see cref="OnBackpressure"/> 强制通知生产方</para>
/// <para>3. Consumer 检查幂等缓存:命中 → TryRestoreFromCache(调 OnSuccess) → 跳过 Handle;未命中 → Handle → OnSuccess</para>
/// <para><b>默认配置+无例外</b>:回调有默认值(<see cref="ActorDefaults"/>),不传也保证双工管道畅通。自定义用 <c>init</c> 属性覆盖。</para>
/// <para><b>环形背压管道</b>:生产方→Actor(请求)→Actor检测水位线→OnBackpressure→生产方停止,形成强制闭环。</para>
/// </summary>
public interface IRequestCommand : IIdempotentCommand {
    /// <summary>背压回调 — 水位线变化时由 ConsumeLoop 调用,强制生产方感知并停止/延迟生产,不可为 null</summary>
    Action<BackpressureSignal> OnBackpressure { get; }

    /// <summary>
    /// 从缓存恢复结果 — 命中缓存时由 ActorBase ConsumeLoop 调用。
    /// <para>实现从 <paramref name="store"/> 取回缓存结果,调用命令自带的 OnSuccess 回调。</para>
    /// </summary>
    /// <param name="store">幂等去重存储</param>
    /// <returns>true=命中缓存并已调用 OnSuccess(跳过 HandleAsync);false=未命中(需执行 HandleAsync)</returns>
    bool TryRestoreFromCache(IIdempotencyStore store);
}

/// <summary>
/// 请求命令 — 携带类型安全的成功/失败回调。
/// <para>命令实现此接口后,Consumer 处理完调用 <see cref="OnSuccess"/> 或异常时调用 <see cref="OnFailure"/>。</para>
/// <para>背压回调 <see cref="IRequestCommand.OnBackpressure"/> 从基接口继承,无需重复声明。</para>
/// <para>回调有默认值(<see cref="ActorDefaults"/>),不传也保证双工管道畅通。自定义用 <c>init</c> 属性覆盖。</para>
/// <para>回调在 Consumer 线程执行,应快速返回;耗时操作应调度到其他线程。</para>
/// </summary>
/// <typeparam name="TReply">回执类型</typeparam>
public interface IRequestCommand<TReply> : IRequestCommand {
    /// <summary>成功回执回调 — Consumer 处理完后调用,不可为 null</summary>
    Action<TReply> OnSuccess { get; }

    /// <summary>失败回调 — Consumer 处理异常时调用,不可为 null</summary>
    Action<Exception> OnFailure { get; }
}
