
namespace Core.Hosting;

public sealed record ServiceMessage
{
    public required string Id { get; init; }
    public required string MessageType { get; init; }
    public required string Sender { get; init; }
    public string? Target { get; init; }
    public required object Payload { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

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

public sealed class ServiceMessageBus : IDisposable
{
    private readonly ConcurrentDictionary<string, ImmutableList<Func<ServiceMessage, Task>>> _subscribers = new();
    private readonly ConcurrentDictionary<string, ImmutableList<ServiceMessage>> _messageHistory = new();
    private readonly int _maxHistoryPerChannel;

    public ServiceMessageBus(int maxHistoryPerChannel = WorkflowConstants.Cache.MaxCacheItems)
    {
        _maxHistoryPerChannel = maxHistoryPerChannel;
    }

    public event Func<ServiceMessage, Task>? MessageReceived;

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

    public Task<IReadOnlyList<ServiceMessage>> GetMessageHistoryAsync(string messageType, int count = 10, CancellationToken ct = default)
    {
        if (_messageHistory.TryGetValue(messageType, out var history))
        {
            var snapshot = history;
            return Task.FromResult<IReadOnlyList<ServiceMessage>>(snapshot.TakeLast(count).ToList());
        }

        return Task.FromResult<IReadOnlyList<ServiceMessage>>(Array.Empty<ServiceMessage>());
    }

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

    public void Dispose()
    {
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


