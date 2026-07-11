namespace Core.Hosting;

/// <summary>
/// 应用级事件总线实现 — 基于 ServiceMessageBus 的强类型包装
/// 对齐 Reasonix event.Sink: 将弱类型 ServiceMessage 转换为强类型 AppEvent
/// </summary>
public sealed class AppEventBus : IAppEventBus
{
    private readonly ServiceMessageBus _messageBus;
    private readonly ConcurrentDictionary<ServiceMessageType, ImmutableList<Action<AppEvent>>> _subscribers = new();
    private readonly object _lock = new();

    public AppEventBus(ServiceMessageBus messageBus)
    {
        _messageBus = messageBus;
        _messageBus.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// 发布应用级事件 — 转换为 ServiceMessage 发布到底层总线
    /// </summary>
    public async Task PublishAsync(AppEvent appEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(appEvent);

        var message = ServiceMessage.Create(
            appEvent.Kind.ToValue(),
            appEvent.Sender ?? "AppEventBus",
            new AppEventPayload
            {
                Kind = appEvent.Kind,
                Detail = appEvent.Detail,
                Data = appEvent.Data,
                SessionId = appEvent.SessionId
            });

        await _messageBus.PublishAsync(message, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 订阅指定类型的事件
    /// </summary>
    public Task<IAsyncDisposable> SubscribeAsync(ServiceMessageType kind, Action<AppEvent> handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _subscribers.AddOrUpdate(
            kind,
            _ => ImmutableList.Create(handler),
            (_, existing) => existing.Add(handler));

        var subscription = new AppEventSubscription(() =>
        {
            _subscribers.AddOrUpdate(
                kind,
                _ => ImmutableList<Action<AppEvent>>.Empty,
                (_, existing) => existing.Remove(handler));
        });

        return Task.FromResult<IAsyncDisposable>(subscription);
    }

    /// <summary>
    /// 订阅所有事件
    /// </summary>
    public Task<IAsyncDisposable> SubscribeAllAsync(Action<AppEvent> handler, CancellationToken ct = default)
    {
        return SubscribeAsync((ServiceMessageType)(-1), handler, ct);
    }

    private Task OnMessageReceived(ServiceMessage message)
    {
        if (message.Payload is AppEventPayload payload)
        {
            var appEvent = new AppEvent
            {
                Kind = payload.Kind,
                Detail = payload.Detail,
                Data = payload.Data,
                Timestamp = message.Timestamp,
                Sender = message.Sender,
                SessionId = payload.SessionId
            };
            NotifySubscribers(appEvent);
        }
        return Task.CompletedTask;
    }

    private void NotifySubscribers(AppEvent appEvent)
    {
        if (_subscribers.TryGetValue(appEvent.Kind, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try { handler(appEvent); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[AppEventBus] Subscriber handler failed: {ex.Message}"); }
            }
        }

        if (_subscribers.TryGetValue((ServiceMessageType)(-1), out var allHandlers))
        {
            foreach (var handler in allHandlers)
            {
                try { handler(appEvent); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[AppEventBus] SubscribeAll handler failed: {ex.Message}"); }
            }
        }
    }
}

/// <summary>
/// AppEvent 内部数据负载 — 序列化到 ServiceMessage.Payload
/// </summary>
internal sealed class AppEventPayload
{
    public ServiceMessageType Kind { get; init; }
    public string? Detail { get; init; }
    public object? Data { get; init; }
    public string? SessionId { get; init; }
}

/// <summary>
/// 订阅取消句柄
/// </summary>
internal sealed class AppEventSubscription(Action unsubscribe) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        unsubscribe();
        return ValueTask.CompletedTask;
    }
}
