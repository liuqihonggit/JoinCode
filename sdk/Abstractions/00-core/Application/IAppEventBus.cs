namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 应用级强类型事件 — 对齐 Reasonix event.Event
/// 将"发生了什么"与"如何显示"解耦
/// </summary>
public sealed record AppEvent
{
    /// <summary>事件类型</summary>
    public required ServiceMessageType Kind { get; init; }

    /// <summary>事件详情（可选）</summary>
    public string? Detail { get; init; }

    /// <summary>强类型数据负载（可选）</summary>
    public object? Data { get; init; }

    /// <summary>事件时间戳</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>发送者标识</summary>
    public string? Sender { get; init; }

    /// <summary>会话 ID（可选）</summary>
    public string? SessionId { get; init; }

    public static AppEvent Create(ServiceMessageType kind, string? detail = null, object? data = null, string? sender = null, string? sessionId = null) => new()
    {
        Kind = kind,
        Detail = detail,
        Data = data,
        Sender = sender,
        SessionId = sessionId
    };
}

/// <summary>
/// 应用级事件总线接口 — 强类型事件发布/订阅
/// 内部可基于 ServiceMessageBus 实现
/// </summary>
public interface IAppEventBus
{
    /// <summary>
    /// 发布应用级事件
    /// </summary>
    Task PublishAsync(AppEvent appEvent, CancellationToken ct = default);

    /// <summary>
    /// 订阅指定类型的事件
    /// </summary>
    Task<IAsyncDisposable> SubscribeAsync(ServiceMessageType kind, Action<AppEvent> handler, CancellationToken ct = default);

    /// <summary>
    /// 订阅所有事件
    /// </summary>
    Task<IAsyncDisposable> SubscribeAllAsync(Action<AppEvent> handler, CancellationToken ct = default);
}
