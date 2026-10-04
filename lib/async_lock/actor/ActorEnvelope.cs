namespace Core.Utils;

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
