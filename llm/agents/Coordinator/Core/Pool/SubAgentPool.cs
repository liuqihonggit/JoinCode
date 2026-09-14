namespace Core.Agents.Coordinator.Pool;

/// <summary>
/// 池化子代理条目 — 记录回池时间和原始任务描述
/// </summary>
internal sealed class PooledAgent
{
    public required AgentBase Agent { get; init; }
    public required DateTimeOffset ReturnedAt { get; init; }
    public required string OriginalTask { get; init; }
}

/// <summary>
/// 子代理代理池 — L3 干预层（ADR 0106）
/// <para>
/// 子代理完成后回池而非直接 Dispose，新任务可抢塞复用上下文（保前缀缓存）。
/// 空闲超时后真正 Dispose，池满时直接 Dispose。
/// </para>
/// </summary>
public sealed partial class SubAgentPool : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, PooledAgent> _pool = new();
    private readonly SubAgentLivenessOptions _options;
    private readonly ILogger? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly PeriodicTimer? _cleanupTimer;
    private readonly CancellationTokenSource _cts = new();
    private Task? _cleanupLoop;

    /// <summary>
    /// 构造子代理代理池
    /// </summary>
    public SubAgentPool(SubAgentLivenessOptions options, ILogger? logger = null, Func<DateTimeOffset>? clock = null)
    {
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
    public void Start()
    {
        _cleanupLoop = Task.Run(CleanupLoopAsync);
    }

    /// <summary>
    /// 子代理完成后回池 — 池满则直接 Dispose
    /// </summary>
    public bool Return(AgentBase agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (_options.PoolMaxSize == 0)
        {
            agent.Dispose();
            return false;
        }

        if (_pool.Count >= _options.PoolMaxSize)
        {
            _logger?.LogDebug("[SubAgentPool] 池满（{Count}/{Max}），直接 Dispose Agent {AgentId}",
                _pool.Count, _options.PoolMaxSize, agent.ObjectId.UniqueId);
            agent.Dispose();
            return false;
        }

        var entry = new PooledAgent
        {
            Agent = agent,
            ReturnedAt = _clock(),
            OriginalTask = agent.Task,
        };

        if (_pool.TryAdd(agent.ObjectId.UniqueId, entry))
        {
            _logger?.LogDebug("[SubAgentPool] Agent {AgentId} 回池（{Count}/{Max}）",
                agent.ObjectId.UniqueId, _pool.Count, _options.PoolMaxSize);
            return true;
        }

        agent.Dispose();
        return false;
    }

    /// <summary>
    /// 抢塞新任务：从池中找上下文最匹配的已完成子代理
    /// </summary>
    public AgentBase? TryAcquire(string taskDescription)
    {
        if (_pool.IsEmpty) return null;

        var best = (PooledAgent?)null;
        var bestScore = -1.0;

        foreach (var (_, entry) in _pool)
        {
            if (entry.Agent.Status != TaskExecutionStatus.Completed &&
                entry.Agent.Status != TaskExecutionStatus.Failed)
                continue;

            var score = ComputeRelevance(entry.OriginalTask, taskDescription);
            if (score > bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }

        if (best is null) return null;

        if (_pool.TryRemove(best.Agent.ObjectId.UniqueId, out _))
        {
            _logger?.LogDebug("[SubAgentPool] Agent {AgentId} 被抢塞，相关性: {Score:F2}",
                best.Agent.ObjectId.UniqueId, bestScore);
            return best.Agent;
        }

        return null;
    }

    /// <summary>
    /// 从池中移除并 Dispose 指定代理
    /// </summary>
    public bool Remove(string agentId)
    {
        if (_pool.TryRemove(agentId, out var entry))
        {
            entry.Agent.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 计算任务相关性评分 — 简单的关键词重叠度
    /// </summary>
    private static double ComputeRelevance(string originalTask, string newTask)
    {
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
    private async Task CleanupLoopAsync()
    {
        try
        {
            while (await _cleanupTimer!.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
            {
                var now = _clock();
                foreach (var (id, entry) in _pool)
                {
                    var idleSeconds = (now - entry.ReturnedAt).TotalSeconds;
                    if (idleSeconds > _options.PoolIdleTimeoutSeconds)
                    {
                        if (_pool.TryRemove(id, out var removed))
                        {
                            removed.Agent.Dispose();
                            _logger?.LogDebug("[SubAgentPool] Agent {AgentId} 空闲超时（{Seconds:F0}s）已 Dispose",
                                id, idleSeconds);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SubAgentPool] 清理循环异常");
        }
    }

    /// <summary>
    /// 释放代理池资源 — Dispose 池中所有代理
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cleanupTimer?.Dispose();
        _cts.Dispose();

        foreach (var (_, entry) in _pool)
            entry.Agent.Dispose();
        _pool.Clear();

        return ValueTask.CompletedTask;
    }
}
