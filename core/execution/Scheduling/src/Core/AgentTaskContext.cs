
namespace Core.Scheduling;

public sealed class AgentTaskContext : IAgentTaskContext
{
    private readonly ConcurrentDictionary<string, JsonElement> _metadata = new();
    private readonly Dictionary<int, StructuredTaskEntry> _structuredTasks = new();
    private readonly SemaphoreSlim _structuredTasksSemaphore = new(1, 1);

    public required string TaskId { get; init; }

    public required int AgentIndex { get; init; }

    public required int TotalAgents { get; init; }

    public required string WorkScope { get; init; }

    public required string TaskName { get; init; }

    public required string Description { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public int Priority { get; init; } = 0;

    public string? ParentTaskId { get; init; }

    public Dictionary<string, JsonElement> GetMetadata() => new(_metadata);

    public async Task<IReadOnlyList<StructuredTaskEntry>> GetStructuredTasksAsync(CancellationToken cancellationToken = default)
    {
        await _structuredTasksSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _structuredTasks.Values.OrderBy(t => t.Order).ToList();
        }
        finally
        {
            _structuredTasksSemaphore.Release();
        }
    }

    public async Task AddStructuredTaskAsync(StructuredTaskEntry task, CancellationToken cancellationToken = default)
    {
        await _structuredTasksSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _structuredTasks[task.Order] = task;
        }
        finally
        {
            _structuredTasksSemaphore.Release();
        }
    }

    public async Task UpdateStructuredTaskAsync(int order, string? result = null, string? status = null, CancellationToken cancellationToken = default)
    {
        await _structuredTasksSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_structuredTasks.TryGetValue(order, out var existing)) return;

            _structuredTasks[order] = existing with
            {
                Result = result ?? existing.Result,
                Status = status ?? existing.Status
            };
        }
        finally
        {
            _structuredTasksSemaphore.Release();
        }
    }

    public async Task ExcludePossibilityAsync(int taskOrder, int possibilityIndex, string reason, CancellationToken cancellationToken = default)
    {
        await _structuredTasksSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_structuredTasks.TryGetValue(taskOrder, out var task)) return;

            if (possibilityIndex < 0 || possibilityIndex >= task.Possibilities.Count) return;

            var possibilities = task.Possibilities.ToList();
            possibilities[possibilityIndex] = possibilities[possibilityIndex] with
            {
                Excluded = true,
                ExclusionReason = reason
            };

            _structuredTasks[taskOrder] = task with { Possibilities = possibilities };
        }
        finally
        {
            _structuredTasksSemaphore.Release();
        }
    }

    public CancellationToken CancellationToken { get; init; }

    public CancellationTokenSource? CancellationTokenSource { get; init; }

    public AgentTaskContext CreateSubContext(
        string subTaskId,
        string subTaskName,
        string subDescription,
        string subWorkScope)
    {
        var subContext = new AgentTaskContext
        {
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

        foreach (var (key, value) in _metadata)
        {
            subContext._metadata[key] = value;
        }

        if (!_structuredTasksSemaphore.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("[SCH001] CreateSubContext: 等待结构化任务信号量超时");
        try
        {
            foreach (var task in _structuredTasks.Values.OrderBy(t => t.Order))
            {
                subContext._structuredTasks[task.Order] = task;
            }
        }
        finally
        {
            _structuredTasksSemaphore.Release();
        }

        return subContext;
    }

    public T? GetMetadataValue<T>(string key, T? defaultValue = default)
    {
        if (!_metadata.TryGetValue(key, out var element))
            return defaultValue;

        try
        {
            if (typeof(T) == typeof(string)) return (T)(object?)element.GetString()!;
            if (typeof(T) == typeof(int)) return (T)(object)element.GetInt32();
            if (typeof(T) == typeof(long)) return (T)(object)element.GetInt64();
            if (typeof(T) == typeof(double)) return (T)(object)element.GetDouble();
            if (typeof(T) == typeof(bool)) return (T)(object)element.GetBoolean();
            if (typeof(T) == typeof(JsonElement)) return (T)(object)element;
            if (typeof(T) == typeof(object)) return (T)DeserializeToObject(element)!;
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public void SetMetadataValue<T>(string key, T value)
    {
        _metadata[key] = JsonElementHelper.FromPrimitives(value);
    }

    public bool IsCancellationRequested => CancellationToken.IsCancellationRequested;

    public void ThrowIfCancellationRequested()
    {
        CancellationToken.ThrowIfCancellationRequested();
    }

    public void Cancel()
    {
        CancellationTokenSource?.Cancel();
    }

    public void CancelAfter(TimeSpan delay)
    {
        CancellationTokenSource?.CancelAfter(delay);
    }

    public CancellationToken CreateLinkedToken(CancellationToken additionalToken)
    {
        if (CancellationTokenSource == null)
        {
            return additionalToken;
        }
        return CancellationTokenSource.Token.CombineWith(additionalToken).Token;
    }

    private static object? DeserializeToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            JsonValueKind.Null => null,
            _ => element.Clone()
        };
    }
}

internal static class CancellationTokenExtensions
{
    public static CancellationTokenSource CombineWith(
        this CancellationToken token1,
        CancellationToken token2)
    {
        return CancellationTokenSource.CreateLinkedTokenSource(token1, token2);
    }
}
