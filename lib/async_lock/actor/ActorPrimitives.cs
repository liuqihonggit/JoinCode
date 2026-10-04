namespace Core.Utils;

/// <summary>
/// 幂等命令标记接口 — 纯开发规约标记,框架不自动处理重试安全。
/// <para><b>⚠️ 框架行为</b>:ActorBase 不读取此接口,不自动缓存或校验幂等性。重试安全由调用方保证。</para>
/// <para><b>与 IRequestCommand 区别</b>:IRequestCommand 携带幂等键+TryRestoreFromCache,框架 ConsumeLoop 自动做缓存命中跳过;本接口仅为文档标记。</para>
/// <para>典型幂等命令:查询(Get/Read)、取消(Cancel)、状态切换到固定值(SetXxx)。</para>
/// <para>非幂等命令:追加(Append)、递增(Increment)、创建(Create) — 重试可能产生重复副作用。</para>
/// </summary>
public interface IIdempotent { }

/// <summary>
/// 单元类型 — 用于不需要输出的 Actor 的 TOut 参数。
/// </summary>
public readonly record struct Unit {
    /// <summary>唯一实例</summary>
    public static readonly Unit Value = default;
}

/// <summary>
/// 重试队列条目 — 命令 + 当前重试次数(P1-2: 单例重试队列)
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
/// <param name="Command">待重试命令</param>
/// <param name="Attempt">当前重试次数(1=首次重试)</param>
/// <param name="Sender">发送者引用(null=无 sender,重试时保留原 sender)</param>
internal sealed record RetryEntry<TCommand>(
    TCommand Command,
    int Attempt,
    object? Sender = null);

/// <summary>
/// 消息信封 — 包装命令 + 发送者引用,供 Channel 传递(Akka Envelope 对齐)。
/// </summary>
/// <typeparam name="T">命令类型</typeparam>
/// <param name="Command">命令</param>
/// <param name="Sender">发送者引用(null=无 sender)</param>
internal readonly record struct MessageEnvelope<T>(
    T Command,
    object? Sender);

/// <summary>
/// 背压重试失败事件参数 — 16次重试后消息仍未能入队
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
/// <param name="Command">未能入队的命令(外部可计入死信队列)</param>
/// <param name="RetryCount">重试次数</param>
public sealed record BackpressureSendFailedEventArgs<TCommand>(
    TCommand Command,
    int RetryCount);

/// <summary>
/// 输出消息丢弃事件参数 — 输出通道满时 TryPublish 丢弃的消息
/// </summary>
/// <typeparam name="TOut">输出消息类型</typeparam>
/// <param name="Message">被丢弃的消息(外部可计入死信队列或重投)</param>
public sealed record OutputDroppedEventArgs<TOut>(TOut Message);

/// <summary>
/// PoisonPill 标记接口 — 实现此接口的消息被 Actor 收到后自动停止(Akka 对齐)。
/// <para>用户的 TCommand 类型需包含一个实现 IPoisonPill 的成员,ActorBase 检测到后触发停止。</para>
/// <para>语义:PoisonPill 排在邮箱中,前面的消息处理完后才停止(保证 FIFO)。</para>
/// </summary>
public interface IPoisonPill { }

/// <summary>
/// Identify 消息接口 — 查询 Actor 身份,Actor 收到后自动回复 <see cref="ActorIdentity"/>(Akka 对齐)。
/// <para>用于查询 Actor 是否存在/可达,获取 Actor 引用。</para>
/// </summary>
public interface IIdentify {
    /// <summary>关联标识 — 匹配请求和回复</summary>
    object? CorrelationId { get; }
}

/// <summary>
/// Identify 消息 — 查询 Actor 身份(Akka 对齐)。
/// <para>Actor 收到后自动回复 <see cref="ActorIdentity"/>,无需用户 Handle 处理。</para>
/// </summary>
/// <param name="CorrelationId">关联标识 — 匹配请求和回复</param>
public sealed record IdentifyMessage(object? CorrelationId) : IIdentify;

/// <summary>
/// Actor 身份回复 — Identify 查询的回复消息(Akka 对齐)。
/// </summary>
/// <param name="CorrelationId">关联标识 — 匹配请求和回复</param>
/// <param name="Subject">Actor 引用(null=Actor 不存在)</param>
public sealed record ActorIdentity(object? CorrelationId, IActorLifecycle? Subject);

/// <summary>
/// Actor 生命周期接口 — 提供释放状态查询(Akka 对齐)。
/// <para>ActorBase 实现此接口,GracefulStop 等工具通过此接口查询 Actor 是否已终止。</para>
/// </summary>
public interface IActorLifecycle : IAsyncDisposable {
    /// <summary>Actor 是否已释放(DisposeAsync 已完成)</summary>
    bool IsDisposed { get; }
}

/// <summary>
/// Actor 上下文接口 — 封装 Self/Sender/IsDisposed 统一入口(Akka 对齐)。
/// <para>在 Handle 中通过 <c>Context.Sender</c> 访问当前消息发送者,通过 <c>Context.Self</c> 访问自身 Id。</para>
/// <para>SupervisedActor 扩展为 <c>ISupervisedActorContext</c>,添加 Children/Watch/Unwatch。</para>
/// </summary>
public interface IActorContext {
    /// <summary>自身 Actor Id — 等同于 ActorBase.Id</summary>
    string Self { get; }

    /// <summary>当前消息发送者 — 等同于 ActorBase.Sender,Handle 中获取 Tell 时传入的 sender</summary>
    object? Sender { get; }

    /// <summary>Actor 是否已释放 — 等同于 ActorBase.IsDisposed</summary>
    bool IsDisposed { get; }
}

/// <summary>
/// Actor Ask 模式死锁异常 — Ask 超时后抛出,带诊断信息指导修复。
/// </summary>
/// <remarks>
/// <para>触发条件:AskAwait 超时(默认10s) — Consumer 未在超时内处理命令并设置 Tcs。</para>
/// <para>常见根因:线程池饥饿 — 所有线程被阻塞等待,Consumer 任务无法被调度。</para>
/// <para>修复指导:Dispose 路径改用 Tell(TrySend);查询路径检查 Consumer 是否阻塞或线程池是否不足。</para>
/// </remarks>
public sealed class ActorAskDeadlockException : TimeoutException {
    /// <summary>Actor 类型名</summary>
    public string ActorName { get; }

    /// <summary>超时毫秒数</summary>
    public int TimeoutMs { get; }

    /// <summary>
    /// 构造 Ask 死锁异常
    /// </summary>
    /// <param name="actorName">Actor 类型名</param>
    /// <param name="timeoutMs">超时毫秒数</param>
    public ActorAskDeadlockException(string actorName, int timeoutMs)
        : base($"Actor {actorName} Ask 超时({timeoutMs}ms) — 可能线程池饥饿导致 Consumer 无法调度。" +
               "Dispose 路径改用 Tell(TrySend);查询路径检查 Consumer 是否阻塞或线程池是否不足。") {
        ActorName = actorName;
        TimeoutMs = timeoutMs;
    }
}

/// <summary>
/// Actor 循环 Ask 异常 — 等待图检测到环时抛出,预防循环 Ask 死锁(类型2)。
/// </summary>
/// <remarks>
/// <para>触发条件:AskAwait 检测到等待图环 — Actor A 等 B 回复,同时 B 等 A 回复。</para>
/// <para>检测机制:静态等待图(wait-for graph),通过线程ID自动识别调用方Actor,记入等待边,检测到环即抛异常。</para>
/// <para>修复指导:打破循环 — 其中一方改用 Tell(不等回复),或重构调用链消除循环依赖。</para>
/// </remarks>
public sealed class ActorCyclicAskException : InvalidOperationException {
    /// <summary>调用方 Actor ID</summary>
    public string CallerActorId { get; }

    /// <summary>目标 Actor ID</summary>
    public string TargetActorId { get; }

    /// <summary>
    /// 构造循环 Ask 异常
    /// </summary>
    /// <param name="callerActorId">调用方 Actor ID</param>
    /// <param name="targetActorId">目标 Actor ID</param>
    public ActorCyclicAskException(string callerActorId, string targetActorId)
        : base($"循环 Ask 检测: Actor {callerActorId} 等 {targetActorId} 回复,同时 {targetActorId} 等 {callerActorId} 回复 → 等待图环 → 死锁。" +
               "修复:其中一方改用 Tell(TrySend,不等回复),或重构调用链消除循环依赖。") {
        CallerActorId = callerActorId;
        TargetActorId = targetActorId;
    }
}
