namespace Core.Agents.Coordinator.Pool;

/// <summary>
/// 池化子代理条目 — 记录回池时间和原始任务描述
/// </summary>
internal sealed class PooledAgent {
    /// <summary>获取代理实例。</summary>
    public required AgentBase Agent { get; init; }
    /// <summary>获取回池时间。</summary>
    public required DateTimeOffset ReturnedAt { get; init; }
    /// <summary>获取原始任务描述。</summary>
    public required string OriginalTask { get; init; }
}

/// <summary>
/// 子代理代理池 — L3 干预层（ADR 0106）
/// <para>
/// 子代理完成后回池而非直接 Dispose，新任务可抢塞复用上下文（保前缀缓存）。
/// 空闲超时后真正 Dispose，池满时直接 Dispose。
/// </para>
/// </summary>
public sealed partial class SubAgentPool : IAsyncDisposable {
    private volatile ImmutableDictionary<string, PooledAgent> _pool = ImmutableDictionary<string, PooledAgent>.Empty;
    private readonly SubAgentLivenessOptions _options;
    private readonly ILogger? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly PeriodicTimer? _cleanupTimer;
    private volatile bool _stopping;
    private Task? _cleanupLoop;
    private bool _disposed;

    /// <summary>
    /// 构造子代理代理池
    /// </summary>
    public SubAgentPool(SubAgentLivenessOptions options, ILogger? logger = null, Func<DateTimeOffset>? clock = null) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _cleanupTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
    }

    /// <summary>池中代理数量</summary>
    public int Count => _pool.Count;

    /// <summary>池是否已满</summary>
    public bool IsFull => _pool.Count >= _options.PoolMaxSize;

    /// <summary>
    /// 启动空闲超时清理循环
    /// </summary>
    public void Start() {
        _logger?.LogInformation("[SubAgentPool] 启动，池上限: {Max}，空闲超时: {Timeout}s",
            _options.PoolMaxSize, _options.PoolIdleTimeoutSeconds);
        _cleanupLoop = Task.Run(CleanupLoopAsync);
    }

    /// <summary>
    /// 子代理完成后回池 — 池满则直接 Dispose
    /// </summary>
    public async Task<bool> Return(AgentBase agent) {
        ArgumentNullException.ThrowIfNull(agent);

        if (_options.PoolMaxSize == 0) {
            _logger?.LogDebug("[SubAgentPool] 代理池已禁用（PoolMaxSize=0），直接 Dispose Agent {AgentId}",
                agent.ObjectId.UniqueId);
            await agent.DisposeAsync().ConfigureAwait(false);
            return false;
        }

        if (_pool.Count >= _options.PoolMaxSize) {
            _logger?.LogDebug("[SubAgentPool] 池满（{Count}/{Max}），直接 Dispose Agent {AgentId}",
                _pool.Count, _options.PoolMaxSize, agent.ObjectId.UniqueId);
            await agent.DisposeAsync().ConfigureAwait(false);
            return false;
        }

        var entry = new PooledAgent {
            Agent = agent,
            ReturnedAt = _clock(),
            OriginalTask = agent.Task,
        };

        if (TryAddPool(agent.ObjectId.UniqueId, entry)) {
            _logger?.LogDebug("[SubAgentPool] Agent {AgentId} 回池（{Count}/{Max}）",
                agent.ObjectId.UniqueId, _pool.Count, _options.PoolMaxSize);
            return true;
        }

        _logger?.LogDebug("[SubAgentPool] Agent {AgentId} 回池失败（TryAdd 竞争），直接 Dispose",
            agent.ObjectId.UniqueId);
        await agent.DisposeAsync().ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// 抢塞新任务：从池中找上下文最匹配的已完成子代理
    /// </summary>
    public AgentBase? TryAcquire(string taskDescription) {
        if (_pool.IsEmpty) return null;

        var best = (PooledAgent?)null;
        var bestScore = -1.0;

        foreach (var (_, entry) in _pool) {
            if (entry.Agent.Status != TaskExecutionStatus.Completed &&
                entry.Agent.Status != TaskExecutionStatus.Failed)
                continue;

            var score = ComputeRelevance(entry.OriginalTask, taskDescription);
            if (score > bestScore) {
                bestScore = score;
                best = entry;
            }
        }

        if (best is null) return null;

        if (TryRemovePool(best.Agent.ObjectId.UniqueId, out _)) {
            _logger?.LogDebug("[SubAgentPool] Agent {AgentId} 被抢塞，相关性: {Score:F2}",
                best.Agent.ObjectId.UniqueId, bestScore);
            return best.Agent;
        }

        return null;
    }

    /// <summary>
    /// 从池中移除并 Dispose 指定代理
    /// </summary>
    public async Task<bool> Remove(string agentId) {
        if (TryRemovePool(agentId, out var entry)) {
            _logger?.LogDebug("[SubAgentPool] Agent {AgentId} 从池中移除并 Dispose", agentId);
            await entry.Agent.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 计算任务相关性评分 — 简单的关键词重叠度
    /// </summary>
    private static double ComputeRelevance(string originalTask, string newTask) {
        if (string.IsNullOrEmpty(originalTask) || string.IsNullOrEmpty(newTask))
            return 0;

        var origWords = originalTask.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var newWords = newTask.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (origWords.Length == 0 || newWords.Length == 0)
            return 0;

        var origSet = origWords.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var overlap = newWords.Count(w => origSet.Contains(w));
        return (double)overlap / Math.Max(origWords.Length, newWords.Length);
    }

    /// <summary>
    /// 空闲超时清理循环
    /// </summary>
    private async Task CleanupLoopAsync() {
        try {
            while (!_stopping && await _cleanupTimer!.WaitForNextTickAsync(CancellationToken.None).ConfigureAwait(false)) {
                if (_stopping) break;
                var now = _clock();
                foreach (var (id, entry) in _pool) {
                    var idleSeconds = (now - entry.ReturnedAt).TotalSeconds;
                    if (idleSeconds > _options.PoolIdleTimeoutSeconds) {
                        if (TryRemovePool(id, out var removed)) {
                            await removed.Agent.DisposeAsync().ConfigureAwait(false);
                            _logger?.LogDebug("[SubAgentPool] Agent {AgentId} 空闲超时（{Seconds:F0}s）已 Dispose",
                                id, idleSeconds);
                        }
                    }
                }
            }
        } catch (OperationCanceledException) { } catch (Exception ex) {
            _logger?.LogError(ex, "[SubAgentPool] 清理循环异常");
        }
    }

    /// <summary>
    /// 释放代理池资源 — 设 _stopping 标志 + Dispose timer，PeriodicTimer.Dispose 让 WaitForNextTickAsync 返回 false，循环安全退出
    /// </summary>
    public ValueTask DisposeAsync() {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _logger?.LogInformation("[SubAgentPool] 释放，Dispose 池中 {Count} 个代理", _pool.Count);
        _stopping = true;
        _cleanupTimer?.Dispose();

        foreach (var (_, entry) in _pool)
            entry.Agent.Dispose();
        Interlocked.Exchange(ref _pool, ImmutableDictionary<string, PooledAgent>.Empty);

        return ValueTask.CompletedTask;
    }

    private bool TryAddPool(string key, PooledAgent value) {
        var current = _pool;
        while (!current.ContainsKey(key)) {
            var updated = current.Add(key, value);
            if (Interlocked.CompareExchange(ref _pool, updated, current) == current) return true;
            current = _pool;
        }
        return false;
    }

    private bool TryRemovePool(string key, out PooledAgent value) {
        value = null!;
        var current = _pool;
        while (current.TryGetValue(key, out var existing)) {
            value = existing;
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _pool, updated, current) == current) return true;
            current = _pool;
        }
        return false;
    }
}