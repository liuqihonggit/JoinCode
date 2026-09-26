namespace Core.Goal;


/// <summary>
/// 节点间冲突消息队列 — 每节点独立 Channel，非阻塞入队/拉取。
/// </summary>
[Register(typeof(IGoalConflictMessenger), ServiceLifetime.Singleton)]
public sealed partial class GoalConflictMessenger : ServiceEntity, IGoalConflictMessenger {
    private ImmutableDictionary<string, Channel<ConflictMessage>> _channels = ImmutableDictionary<string, Channel<ConflictMessage>>.Empty.WithComparers(StringComparer.Ordinal);

    private readonly ILogger<GoalConflictMessenger>? _logger;

    /// <summary>
    /// 构造 GoalConflictMessenger — 注入可选日志记录器
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public GoalConflictMessenger(ILogger<GoalConflictMessenger>? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask EnqueueConflictAsync(ConflictMessage message, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(message);
        var channel = GetOrAddChannel(message.TargetNodeId);
        await channel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[GoalConflictMessenger] 入队冲突: {Source} → {Target}: {Content}",
            message.SourceNodeId, message.TargetNodeId, message.Content);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ConflictMessage>> DequeueConflictsAsync(string nodeId, CancellationToken cancellationToken = default) {
        if (!Volatile.Read(ref _channels).TryGetValue(nodeId, out var channel))
            return [];

        var messages = new List<ConflictMessage>();
        while (channel.Reader.TryRead(out var message)) {
            messages.Add(message);
        }

        if (messages.Count > 0) {
            _logger?.LogDebug("[GoalConflictMessenger] 拉取冲突: {NodeId} 共 {Count} 条", nodeId, messages.Count);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return messages;
    }

    /// <inheritdoc />
    public int GetPendingCount(string nodeId) {
        if (!Volatile.Read(ref _channels).TryGetValue(nodeId, out var channel))
            return 0;
        return channel.Reader.Count;
    }

    private Channel<ConflictMessage> GetOrAddChannel(string nodeId) {
        var snapshot = Volatile.Read(ref _channels);
        if (snapshot.TryGetValue(nodeId, out var existing))
            return existing;

        var newChannel = Channel.CreateBounded<ConflictMessage>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.Wait });
        ImmutableInterlocked.Update(ref _channels, d => d.ContainsKey(nodeId) ? d : d.Add(nodeId, newChannel));
        return Volatile.Read(ref _channels)[nodeId];
    }
}