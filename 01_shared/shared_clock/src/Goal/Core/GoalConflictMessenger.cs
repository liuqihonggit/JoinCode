namespace Core.Goal;


/// <summary>
/// 节点间冲突消息队列 — 每节点独立 Channel，非阻塞入队/拉取。
/// </summary>
[Register(typeof(IGoalConflictMessenger), ServiceLifetime.Singleton)]
public sealed partial class GoalConflictMessenger : ServiceEntity, IGoalConflictMessenger
{
    private readonly ConcurrentDictionary<string, Channel<ConflictMessage>> _channels = new(StringComparer.Ordinal);

    private readonly ILogger<GoalConflictMessenger>? _logger;

    public GoalConflictMessenger(ILogger<GoalConflictMessenger>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask EnqueueConflictAsync(ConflictMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var channel = _channels.GetOrAdd(message.TargetNodeId, _ => Channel.CreateUnbounded<ConflictMessage>());
        await channel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[GoalConflictMessenger] 入队冲突: {Source} → {Target}: {Content}",
            message.SourceNodeId, message.TargetNodeId, message.Content);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ConflictMessage>> DequeueConflictsAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(nodeId, out var channel))
            return [];

        var messages = new List<ConflictMessage>();
        while (channel.Reader.TryRead(out var message))
        {
            messages.Add(message);
        }

        if (messages.Count > 0)
        {
            _logger?.LogDebug("[GoalConflictMessenger] 拉取冲突: {NodeId} 共 {Count} 条", nodeId, messages.Count);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return messages;
    }

    /// <inheritdoc />
    public int GetPendingCount(string nodeId)
    {
        if (!_channels.TryGetValue(nodeId, out var channel))
            return 0;
        return channel.Reader.Count;
    }
}
