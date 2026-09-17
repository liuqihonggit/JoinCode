
namespace Core.Hosting;

/// <summary>
/// 服务消息记录 — 描述消息总线中传递的消息
/// </summary>
public sealed record ServiceMessage
{
    /// <summary>消息唯一标识</summary>
    public required string Id { get; init; }
    /// <summary>消息类型</summary>
    public required string MessageType { get; init; }
    /// <summary>发送方名称</summary>
    public required string Sender { get; init; }
    /// <summary>目标方名称，可选</summary>
    public string? Target { get; init; }
    /// <summary>消息负载</summary>
    public required object Payload { get; init; }
    /// <summary>消息时间戳</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 创建服务消息 — 自动生成唯一标识
    /// </summary>
    /// <param name="messageType">消息类型</param>
    /// <param name="sender">发送方名称</param>
    /// <param name="payload">消息负载</param>
    /// <param name="target">目标方名称，可选</param>
    /// <returns>新创建的服务消息</returns>
    public static ServiceMessage Create(string messageType, string sender, object payload, string? target = null)
    {
        return new ServiceMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            MessageType = messageType,
            Sender = sender,
            Target = target,
            Payload = payload
        };
    }
}

/// <summary>
/// 服务消息总线 — 进程内发布/订阅，带消息历史记录
/// </summary>
public sealed class ServiceMessageBus : IDisposable
{
    private readonly ConcurrentDictionary<string, ImmutableList<Func<ServiceMessage, Task>>> _subscribers = new();
    private readonly ConcurrentDictionary<string, ImmutableList<ServiceMessage>> _messageHistory = new();
    private readonly int _maxHistoryPerChannel;
    private bool _disposed;

    /// <summary>
    /// 构造 ServiceMessageBus — 指定每通道最大历史记录数
    /// </summary>
    /// <param name="maxHistoryPerChannel">每通道最大历史记录数，缺省使用 WorkflowConstants.Cache.MaxCacheItems</param>
    public ServiceMessageBus(int maxHistoryPerChannel = WorkflowConstants.Cache.MaxCacheItems)
    {
        _maxHistoryPerChannel = maxHistoryPerChannel;
    }

    /// <summary>消息接收事件 — 每条消息发布时触发</summary>
    public event Func<ServiceMessage, Task>? MessageReceived;

    /// <summary>
    /// 异步发布消息 — 记录历史并通知订阅者与 MessageReceived 事件
    /// </summary>
    /// <param name="message">待发布消息</param>
    /// <param name="ct">取消令牌</param>
    public async Task PublishAsync(ServiceMessage message, CancellationToken ct = default)
    {
        _messageHistory.AddOrUpdate(
            message.MessageType,
            _ => ImmutableList.Create(message),
            (_, existing) =>
            {
                var updated = existing.Add(message);
                while (updated.Count > _maxHistoryPerChannel)
                {
                    updated = updated.RemoveAt(0);
                }
                return updated;
            });

        if (_subscribers.TryGetValue(message.MessageType, out var handlers))
        {
            var snapshot = handlers;
            var tasks = snapshot.Select(h => h(message)).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        if (MessageReceived != null)
        {
            await MessageReceived(message).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 异步订阅指定类型消息 — 返回可释放的订阅句柄
    /// </summary>
    /// <param name="messageType">消息类型</param>
    /// <param name="handler">消息处理委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>订阅句柄，释放时取消订阅</returns>
    public Task<IAsyncDisposable> SubscribeAsync(string messageType, Func<ServiceMessage, Task> handler, CancellationToken ct = default)
    {
        _subscribers.AddOrUpdate(
            messageType,
            _ => ImmutableList.Create(handler),
            (_, existing) => existing.Contains(handler) ? existing : existing.Add(handler));

        return Task.FromResult<IAsyncDisposable>(new SubscriptionDisposable(messageType, handler, this));
    }

    internal void Unsubscribe(string messageType, Func<ServiceMessage, Task> handler)
    {
        _subscribers.AddOrUpdate(
            messageType,
            _ => ImmutableList<Func<ServiceMessage, Task>>.Empty,
            (_, existing) => existing.Remove(handler));
    }

    /// <summary>
    /// 异步获取指定类型消息的历史记录
    /// </summary>
    /// <param name="messageType">消息类型</param>
    /// <param name="count">返回最近的消息数，缺省 10</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>历史消息列表</returns>
    public Task<IReadOnlyList<ServiceMessage>> GetMessageHistoryAsync(string messageType, int count = 10, CancellationToken ct = default)
    {
        if (_messageHistory.TryGetValue(messageType, out var history))
        {
            var snapshot = history;
            return Task.FromResult<IReadOnlyList<ServiceMessage>>(snapshot.TakeLast(count).ToList());
        }

        return Task.FromResult<IReadOnlyList<ServiceMessage>>(Array.Empty<ServiceMessage>());
    }

    /// <summary>
    /// 异步清除消息历史 — 指定类型则仅清除该类型，null 则清除全部
    /// </summary>
    /// <param name="messageType">消息类型，null 清除全部</param>
    /// <param name="ct">取消令牌</param>
    public Task ClearHistoryAsync(string? messageType = null, CancellationToken ct = default)
    {
        if (messageType != null)
        {
            _messageHistory.TryRemove(messageType, out _);
        }
        else
        {
            _messageHistory.Clear();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放消息总线 — 清空订阅者与历史记录
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscribers.Clear();
        _messageHistory.Clear();
    }

    private sealed class SubscriptionDisposable : IAsyncDisposable
    {
        private readonly string _messageType;
        private readonly Func<ServiceMessage, Task> _handler;
        private readonly ServiceMessageBus _bus;
        private int _disposed;

        public SubscriptionDisposable(string messageType, Func<ServiceMessage, Task> handler, ServiceMessageBus bus)
        {
            _messageType = messageType;
            _handler = handler;
            _bus = bus;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _bus.Unsubscribe(_messageType, _handler);
            }

            return ValueTask.CompletedTask;
        }
    }
}


