namespace JoinCode.Abstractions.Interfaces;

public interface IAgentTaskContext {
    /// <summary>获取任务标识。</summary>
    string TaskId { get; }

    /// <summary>获取当前 Agent 索引。</summary>
    int AgentIndex { get; }

    /// <summary>获取总 Agent 数。</summary>
    int TotalAgents { get; }

    /// <summary>获取工作范围。</summary>
    string WorkScope { get; }

    /// <summary>获取任务名称。</summary>
    string TaskName { get; }

    /// <summary>获取任务描述。</summary>
    string Description { get; }

    /// <summary>获取创建时间。</summary>
    DateTime CreatedAt { get; }

    /// <summary>获取任务优先级。</summary>
    int Priority { get; }

    /// <summary>获取父任务标识。</summary>
    string? ParentTaskId { get; }

    /// <summary>获取元数据字典。</summary>
    Dictionary<string, JsonElement> GetMetadata();

    /// <summary>异步获取结构化任务列表。</summary>
    Task<IReadOnlyList<StructuredTaskEntry>> GetStructuredTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>获取元数据中指定键的值,不存在时返回默认值。</summary>
    T? GetMetadataValue<T>(string key, T? defaultValue = default);

    /// <summary>设置元数据中指定键的值。</summary>
    void SetMetadataValue<T>(string key, T value);

    /// <summary>异步添加结构化任务。</summary>
    Task AddStructuredTaskAsync(StructuredTaskEntry task, CancellationToken cancellationToken = default);

    /// <summary>异步更新结构化任务的状态或结果。</summary>
    Task UpdateStructuredTaskAsync(int order, string? result = null, string? status = null, CancellationToken cancellationToken = default);

    /// <summary>异步排除指定任务的可能性分支并记录原因。</summary>
    Task ExcludePossibilityAsync(int taskOrder, int possibilityIndex, string reason, CancellationToken cancellationToken = default);
}
