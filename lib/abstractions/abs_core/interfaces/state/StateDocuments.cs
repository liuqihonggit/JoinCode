namespace JoinCode.Abstractions.State;

/// <summary>
/// AppState 持久化文档类
/// </summary>
public sealed record AppStateDocument {
    /// <summary>获取或设置文档标识。</summary>
    public string Id { get; init; } = "current";
    /// <summary>获取或设置会话状态。</summary>
    public SessionStateDocument Session { get; init; } = new();
    /// <summary>获取或设置 Agent 状态字典。</summary>
    public Dictionary<string, AgentStateDocument> Agents { get; init; } = new();
    /// <summary>获取或设置任务状态字典。</summary>
    public Dictionary<string, TaskStateDocument> Tasks { get; init; } = new();
    /// <summary>获取或设置配置状态。</summary>
    public ConfigStateDocument Config { get; init; } = new();
    /// <summary>获取或设置保存时间。</summary>
    public DateTime SavedAt { get; init; }
    /// <summary>获取或设置文档版本。</summary>
    public int Version { get; init; } = 1;
}

/// <summary>
/// 会话状态文档
/// </summary>
public sealed class SessionStateDocument {
    /// <summary>获取或设置会话标识。</summary>
    public string SessionId { get; init; } = string.Empty;
    /// <summary>获取或设置系统提示词。</summary>
    public string SystemPrompt { get; init; } = string.Empty;
    /// <summary>获取或设置消息列表。</summary>
    public IEnumerable<ApiMessageDocument> MessageList { get; init; } = Array.Empty<ApiMessageDocument>();
    /// <summary>获取或设置会话开始时间。</summary>
    public DateTime StartedAt { get; init; }
    /// <summary>获取或设置最近活动时间。</summary>
    public DateTime LastActivityAt { get; init; }
    /// <summary>获取或设置当前模型名称。</summary>
    public string? CurrentModel { get; init; }
    /// <summary>获取或设置是否为计划模式。</summary>
    public bool IsPlanMode { get; init; }
    /// <summary>获取或设置当前计划内容。</summary>
    public string? CurrentPlan { get; init; }
}

/// <summary>
/// 聊天消息文档
/// </summary>
public sealed class ApiMessageDocument : ChatMessage {
    /// <summary>获取或设置元数据字典。</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// Agent 状态文档
/// </summary>
public sealed class AgentStateDocument {
    /// <summary>获取或设置 Agent 标识。</summary>
    public string AgentId { get; init; } = string.Empty;
    /// <summary>获取或设置 Agent 名称。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>获取或设置 Agent 类型。</summary>
    public string AgentType { get; init; } = string.Empty;
    /// <summary>获取或设置 Agent 角色。</summary>
    public AgentRole Role { get; init; }
    /// <summary>获取或设置执行器变体。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>获取或设置 Agent 状态。</summary>
    public AgentStatus Status { get; init; }
    /// <summary>获取或设置工作目录。</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>获取或设置当前任务标识。</summary>
    public string? CurrentTaskId { get; init; }
    /// <summary>获取或设置元数据字典。</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
    /// <summary>获取或设置最近活动时间。</summary>
    public DateTime LastActivityAt { get; init; }
}

/// <summary>
/// 任务状态文档
/// </summary>
public sealed class TaskStateDocument {
    /// <summary>获取或设置任务标识。</summary>
    public string TaskId { get; init; } = string.Empty;
    /// <summary>获取或设置任务名称。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>获取或设置任务描述。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>获取或设置任务执行状态。</summary>
    public TaskExecutionStatus Status { get; init; }
    /// <summary>获取或设置负责 Agent 标识。</summary>
    public string? AgentId { get; init; }
    /// <summary>获取或设置父任务标识。</summary>
    public string? ParentTaskId { get; init; }
    /// <summary>获取或设置子任务标识列表。</summary>
    public IEnumerable<string> SubTaskIds { get; init; } = [];
    /// <summary>获取或设置任务进度。</summary>
    public int Progress { get; init; }
    /// <summary>获取或设置任务结果。</summary>
    public string? Result { get; init; }
    /// <summary>获取或设置错误信息。</summary>
    public string? Error { get; init; }
    /// <summary>获取或设置创建时间。</summary>
    public DateTime CreatedAt { get; init; }
    /// <summary>获取或设置开始时间。</summary>
    public DateTime? StartedAt { get; init; }
    /// <summary>获取或设置完成时间。</summary>
    public DateTime? CompletedAt { get; init; }
    /// <summary>获取或设置元数据字典。</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// 配置状态文档
/// </summary>
public sealed class ConfigStateDocument {
    /// <summary>获取或设置是否启用调试日志。</summary>
    public bool DebugLog { get; init; }
    /// <summary>获取或设置是否为简洁模式。</summary>
    public bool IsBriefMode { get; init; }
    /// <summary>获取或设置主题名称。</summary>
    public string Theme { get; init; } = "default";
    /// <summary>获取或设置是否自动确认。</summary>
    public bool AutoConfirm { get; init; }
    /// <summary>获取或设置最大 Token 预算。</summary>
    public long? MaxTokenBudget { get; init; }
    /// <summary>获取或设置已使用 Token 数。</summary>
    public long UsedTokens { get; init; }
    /// <summary>获取或设置设置字典。</summary>
    public Dictionary<string, string> Settings { get; init; } = [];
}

/// <summary>
/// Store 持久化接口
/// </summary>
public interface IStorePersistence<TState> : IStore where TState : notnull {
    /// <summary>
    /// 保存状态
    /// </summary>
    Task SaveAsync(TState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载状态
    /// </summary>
    Task<TState?> LoadAsync(CancellationToken cancellationToken = default);
}
