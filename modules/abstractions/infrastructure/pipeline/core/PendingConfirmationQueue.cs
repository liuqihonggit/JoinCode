namespace Infrastructure.Pipeline;

/// <summary>
/// 后台待确认队列实现 — 与 <see cref="AgentNotificationQueue"/> 同样按目标代理分桶；
/// 后台只登记票据并异步等待，前台通过 <see cref="Snapshot"/> 批量展示后 <see cref="TryResolve"/> 结算。
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IPendingConfirmationQueue), ServiceLifetime.Singleton)]
public sealed partial class PendingConfirmationQueue : ServiceEntity, JoinCode.Abstractions.Interfaces.IPendingConfirmationQueue {
    private readonly ConcurrentDictionary<string, Entry> _pending = new(StringComparer.Ordinal);
    private readonly ILogger<PendingConfirmationQueue>? _logger;

    private sealed record Entry(
        JoinCode.Abstractions.Interfaces.PendingConfirmationItem Item,
        TaskCompletionSource<JoinCode.Abstractions.Interfaces.PendingConfirmationDecision> Completion);

    /// <summary>创建待确认队列。</summary>
    /// <param name="logger">可选日志器。</param>
    public PendingConfirmationQueue(ILogger<PendingConfirmationQueue>? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc/>
    public JoinCode.Abstractions.Interfaces.PendingConfirmationItem Enqueue(
        string? agentId,
        string toolName,
        string reason,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JoinCode.Abstractions.Interfaces.PendingConfirmationDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new JoinCode.Abstractions.Interfaces.PendingConfirmationItem {
            Id = id,
            AgentId = agentId,
            ToolName = toolName,
            Reason = reason,
            Arguments = arguments,
            Decision = completion.Task,
        };

        _pending[id] = new Entry(item, completion);
        _logger?.LogDebug("[PendingConfirmationQueue] 登记: id={Id}, agent={Agent}, tool={Tool}", id, agentId ?? "(main)", toolName);
        Diag.WriteLine($"[PENDING-CONFIRM-QUEUE] enqueue id={id} agent={agentId ?? "(main)"} tool={toolName}");
        return item;
    }

    /// <inheritdoc/>
    public IReadOnlyList<JoinCode.Abstractions.Interfaces.PendingConfirmationItem> Snapshot() =>
        [.. _pending.Values.Select(static entry => entry.Item).OrderBy(static item => item.CreatedAtUtc)];

    /// <inheritdoc/>
    public bool TryResolve(string id, JoinCode.Abstractions.Interfaces.PendingConfirmationDecision decision) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // Defer = 后台已让出等待，但票据仍保留给前台展示；Approve/Deny 才真正结算并移除。
        if (decision == JoinCode.Abstractions.Interfaces.PendingConfirmationDecision.Defer) {
            if (!_pending.TryGetValue(id, out var deferred)) {
                return false;
            }

            var completed = deferred.Completion.TrySetResult(decision);
            if (completed) {
                Diag.WriteLine($"[PENDING-CONFIRM-QUEUE] defer id={id} (kept for foreground)");
            }
            return completed;
        }

        if (!_pending.TryRemove(id, out var entry)) {
            return false;
        }

        entry.Completion.TrySetResult(decision);
        _logger?.LogDebug("[PendingConfirmationQueue] 结算: id={Id}, decision={Decision}", id, decision);
        Diag.WriteLine($"[PENDING-CONFIRM-QUEUE] resolve id={id} decision={decision}");
        return true;
    }

    /// <inheritdoc/>
    public bool HasPending => !_pending.IsEmpty;

    /// <summary>释放队列 — 未结算票据统一 Defer，避免后台等待任务悬挂。</summary>
    public override void Dispose() {
        foreach (var (id, entry) in _pending) {
            if (_pending.TryRemove(id, out var removed)) {
                removed.Completion.TrySetResult(JoinCode.Abstractions.Interfaces.PendingConfirmationDecision.Defer);
            }
        }
        base.Dispose();
    }
}
