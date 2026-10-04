namespace Core.Utils;

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
