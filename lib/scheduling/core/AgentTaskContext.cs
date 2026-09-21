
namespace Core.Scheduling;

/// <summary>
/// 智能体任务上下文 — 封装单个智能体执行任务所需的元数据、结构化子任务与取消控制
/// </summary>
public sealed class AgentTaskContext : IAgentTaskContext {
    private readonly ConcurrentDictionary<string, JsonElement> _metadata = new();
    private readonly Dictionary<int, StructuredTaskEntry> _structuredTasks = new();
    private readonly AsyncLock _structuredTasksSemaphore = new();

    /// <inheritdoc/>
    public required string TaskId { get; init; }

    /// <inheritdoc/>
    public required int AgentIndex { get; init; }

    /// <inheritdoc/>
    public required int TotalAgents { get; init; }

    /// <inheritdoc/>
    public required string WorkScope { get; init; }

    /// <inheritdoc/>
    public required string TaskName { get; init; }

    /// <inheritdoc/>
    public required string Description { get; init; }

    /// <inheritdoc/>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <inheritdoc/>
    public int Priority { get; init; } = 0;

    /// <inheritdoc/>
    public string? ParentTaskId { get; init; }

    /// <inheritdoc/>
    public Dictionary<string, JsonElement> GetMetadata() => new(_metadata);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StructuredTaskEntry>> GetStructuredTasksAsync(CancellationToken cancellationToken = default) {
        using var guard = _structuredTasksSemaphore.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_structuredTasksSemaphore.Name}' 等待超时");

        return _structuredTasks.Values.OrderBy(t => t.Order).ToList();

    }

    /// <inheritdoc/>
    public async Task AddStructuredTaskAsync(StructuredTaskEntry task, CancellationToken cancellationToken = default) {
        using var guard = _structuredTasksSemaphore.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_structuredTasksSemaphore.Name}' 等待超时");

        _structuredTasks[task.Order] = task;

    }

    /// <inheritdoc/>
    public async Task UpdateStructuredTaskAsync(int order, string? result = null, string? status = null, CancellationToken cancellationToken = default) {
        using var guard = _structuredTasksSemaphore.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_structuredTasksSemaphore.Name}' 等待超时");

        if (!_structuredTasks.TryGetValue(order, out var existing)) return;

        _structuredTasks[order] = existing with {
            Result = result ?? existing.Result,
            Status = status ?? existing.Status
        };

    }

    /// <inheritdoc/>
    public async Task ExcludePossibilityAsync(int taskOrder, int possibilityIndex, string reason, CancellationToken cancellationToken = default) {
        using var guard = _structuredTasksSemaphore.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_structuredTasksSemaphore.Name}' 等待超时");

        if (!_structuredTasks.TryGetValue(taskOrder, out var task)) return;

        if (possibilityIndex < 0 || possibilityIndex >= task.Possibilities.Count) return;

        var possibilities = task.Possibilities.ToList();
        possibilities[possibilityIndex] = possibilities[possibilityIndex] with {
            Excluded = true,
            ExclusionReason = reason
        };

        _structuredTasks[taskOrder] = task with { Possibilities = possibilities };

    }

    /// <summary>取消令牌,用于协作式取消任务执行</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>取消令牌源,为空时无法主动取消;非空时可触发取消信号</summary>
    public CancellationTokenSource? CancellationTokenSource { get; init; }

    /// <summary>
    /// 创建子任务上下文 — 继承当前上下文的元数据、结构化任务与取消控制
    /// </summary>
    /// <param name="subTaskId">子任务Id</param>
    /// <param name="subTaskName">子任务名称</param>
    /// <param name="subDescription">子任务描述</param>
    /// <param name="subWorkScope">子任务工作范围</param>
    /// <returns>新的子任务上下文</returns>
    public AgentTaskContext CreateSubContext(
        string subTaskId,
        string subTaskName,
        string subDescription,
        string subWorkScope) {
        var subContext = new AgentTaskContext {
            TaskId = subTaskId,
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = subWorkScope,
            TaskName = subTaskName,
            Description = subDescription,
            Priority = Priority,
            ParentTaskId = TaskId,
            CancellationToken = CancellationToken,
            CancellationTokenSource = CancellationTokenSource
        };

        foreach (var (key, value) in _metadata) {
            subContext._metadata[key] = value;
        }

        using var guard = _structuredTasksSemaphore.TryLock();
        if (guard is null)
            throw new TimeoutException("[SCH001] CreateSubContext: 等待结构化任务信号量超时");
        foreach (var task in _structuredTasks.Values.OrderBy(t => t.Order)) {
            subContext._structuredTasks[task.Order] = task;
        }

        return subContext;
    }

    /// <inheritdoc/>
    public T? GetMetadataValue<T>(string key, T? defaultValue = default) {
        if (!_metadata.TryGetValue(key, out var element))
            return defaultValue;

        try {
            if (typeof(T) == typeof(string)) return (T)(object?)element.GetString()!;
            if (typeof(T) == typeof(int)) return (T)(object)element.GetInt32();
            if (typeof(T) == typeof(long)) return (T)(object)element.GetInt64();
            if (typeof(T) == typeof(double)) return (T)(object)element.GetDouble();
            if (typeof(T) == typeof(bool)) return (T)(object)element.GetBoolean();
            if (typeof(T) == typeof(JsonElement)) return (T)(object)element;
            if (typeof(T) == typeof(object)) return (T)DeserializeToObject(element)!;
            return defaultValue;
        } catch {
            return defaultValue;
        }
    }

    /// <inheritdoc/>
    public void SetMetadataValue<T>(string key, T value) {
        _metadata[key] = JsonElementHelper.FromPrimitives(value);
    }

    /// <summary>是否已请求取消任务</summary>
    public bool IsCancellationRequested => CancellationToken.IsCancellationRequested;

    /// <summary>若已请求取消则抛出取消异常</summary>
    public void ThrowIfCancellationRequested() {
        CancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>触发取消信号,通知协作方停止执行</summary>
    public void Cancel() {
        CancellationTokenSource?.Cancel();
    }

    /// <summary>
    /// 在指定延迟后触发取消信号
    /// </summary>
    /// <param name="delay">延迟时长</param>
    public void CancelAfter(TimeSpan delay) {
        CancellationTokenSource?.CancelAfter(delay);
    }

    /// <summary>
    /// 创建与额外令牌链接的取消令牌,任一令牌取消时新令牌即取消
    /// </summary>
    /// <param name="additionalToken">要链接的额外取消令牌</param>
    /// <returns>链接后的取消令牌</returns>
    public CancellationToken CreateLinkedToken(CancellationToken additionalToken) {
        if (CancellationTokenSource == null) {
            return additionalToken;
        }
        return CancellationTokenSource.Token.CombineWith(additionalToken).Token;
    }

    private static object? DeserializeToObject(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            JsonValueKind.Null => null,
            _ => element.Clone()
        };
    }
}

internal static class CancellationTokenExtensions {
    /// <summary>将两个取消令牌合并为链接令牌源。</summary>
    public static CancellationTokenSource CombineWith(
        this CancellationToken token1,
        CancellationToken token2) {
        return CancellationTokenSource.CreateLinkedTokenSource(token1, token2);
    }
}