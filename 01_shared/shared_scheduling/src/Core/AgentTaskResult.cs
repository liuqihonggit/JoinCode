
namespace Core.Scheduling;

/// <summary>
/// Agent 任务结果实现类 - 包含任务执行结果和 Agent 信息
/// </summary>
public sealed class AgentTaskResult : IAgentTaskResult
{
    private readonly ConcurrentDictionary<string, JsonElement> _metadata = new();

    /// <inheritdoc />
    public required string TaskId { get; init; }

    /// <inheritdoc />
    public required bool IsSuccess { get; init; }

    /// <inheritdoc />
    public required string Output { get; init; }

    /// <inheritdoc />
    public string? Error { get; init; }

    /// <inheritdoc />
    public required long ExecutionTimeMs { get; init; }

    /// <inheritdoc />
    public DateTime StartedAt { get; init; }

    /// <inheritdoc />
    public DateTime CompletedAt { get; init; }

    /// <summary>
    /// 执行任务的 Agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Agent 名称
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>
    /// Agent 索引位置
    /// </summary>
    public int AgentIndex { get; init; }

    /// <inheritdoc />
    public Dictionary<string, JsonElement> GetMetadata() => new(_metadata);

    /// <summary>
    /// 创建成功结果
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="output">输出内容</param>
    /// <param name="executionTimeMs">执行耗时</param>
    /// <returns>任务结果</returns>
    public static AgentTaskResult Success(
        string taskId,
        string agentId,
        string output,
        long executionTimeMs)
    {
        return new AgentTaskResult
        {
            TaskId = taskId,
            AgentId = agentId,
            IsSuccess = true,
            Output = output,
            ExecutionTimeMs = executionTimeMs,
            StartedAt = DateTime.UtcNow.AddMilliseconds(-executionTimeMs),
            CompletedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="error">错误信息</param>
    /// <param name="executionTimeMs">执行耗时</param>
    /// <returns>任务结果</returns>
    public static AgentTaskResult Failure(
        string taskId,
        string agentId,
        string error,
        long executionTimeMs = 0)
    {
        return new AgentTaskResult
        {
            TaskId = taskId,
            AgentId = agentId,
            IsSuccess = false,
            Output = string.Empty,
            Error = error,
            ExecutionTimeMs = executionTimeMs,
            StartedAt = DateTime.UtcNow.AddMilliseconds(-executionTimeMs),
            CompletedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
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
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// 添加元数据
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">键名</param>
    /// <param name="value">值</param>
    /// <returns>当前结果实例（用于链式调用）</returns>
    public AgentTaskResult WithMetadata<T>(string key, T value)
    {
        _metadata[key] = JsonElementHelper.FromPrimitives(value);
        return this;
    }

    /// <summary>
    /// 批量添加元数据
    /// </summary>
    /// <param name="metadata">元数据字典</param>
    /// <returns>当前结果实例（用于链式调用）</returns>
    public AgentTaskResult WithMetadata(Dictionary<string, JsonElement> metadata)
    {
        foreach (var (key, value) in metadata)
        {
            _metadata[key] = value;
        }
        return this;
    }

    /// <summary>
    /// 设置 Agent 信息，返回新实例
    /// </summary>
    /// <param name="agentName">Agent 名称</param>
    /// <param name="agentIndex">Agent 索引</param>
    /// <returns>新的结果实例</returns>
    public AgentTaskResult WithAgentInfo(string agentName, int agentIndex)
    {
        var newResult = new AgentTaskResult
        {
            TaskId = TaskId,
            AgentId = AgentId,
            AgentName = agentName,
            AgentIndex = agentIndex,
            IsSuccess = IsSuccess,
            Output = Output,
            Error = Error,
            ExecutionTimeMs = ExecutionTimeMs,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt
        };

        foreach (var (key, value) in _metadata)
        {
            newResult._metadata[key] = value;
        }

        return newResult;
    }

    /// <summary>
    /// 转换为字符串表示
    /// </summary>
    public override string ToString()
    {
        var status = IsSuccess ? "成功" : "失败";
        return $"[{status}] 任务 {TaskId} (Agent: {AgentId}, 耗时: {ExecutionTimeMs}ms)";
    }
}




