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
