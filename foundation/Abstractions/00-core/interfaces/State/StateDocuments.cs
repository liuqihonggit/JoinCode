namespace JoinCode.Abstractions.State;

/// <summary>
/// AppState 持久化文档类
/// </summary>
public sealed class AppStateDocument
{
    public string Id { get; set; } = "current";
    public SessionStateDocument Session { get; set; } = new();
    public Dictionary<string, AgentStateDocument> Agents { get; set; } = new();
    public Dictionary<string, TaskStateDocument> Tasks { get; set; } = new();
    public ConfigStateDocument Config { get; set; } = new();
    public DateTime SavedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>
/// 会话状态文档
/// </summary>
public sealed class SessionStateDocument
{
    public string SessionId { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public IReadOnlyList<ApiMessageDocument> MessageList { get; set; } = Array.Empty<ApiMessageDocument>();
    public DateTime StartedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public string? CurrentModel { get; set; }
    public bool IsPlanMode { get; set; }
    public string? CurrentPlan { get; set; }
}

/// <summary>
/// 聊天消息文档
/// </summary>
public sealed class ApiMessageDocument : ChatMessage
{
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Agent 状态文档
/// </summary>
public sealed class AgentStateDocument
{
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public AgentStatus Status { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? CurrentTaskId { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public DateTime LastActivityAt { get; set; }
}

/// <summary>
/// 任务状态文档
/// </summary>
public sealed class TaskStateDocument
{
    public string TaskId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TaskExecutionStatus Status { get; set; }
    public string? AgentId { get; set; }
    public string? ParentTaskId { get; set; }
    public IReadOnlyList<string>? SubTaskIds { get; set; }
    public int Progress { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// 配置状态文档
/// </summary>
public sealed class ConfigStateDocument
{
    public bool Verbose { get; set; }
    public bool IsBriefMode { get; set; }
    public string Theme { get; set; } = "default";
    public bool AutoConfirm { get; set; }
    public long? MaxTokenBudget { get; set; }
    public long UsedTokens { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
}

/// <summary>
/// Store 持久化接口
/// </summary>
public interface IStorePersistence<TState> where TState : notnull
{
    /// <summary>
    /// 保存状态
    /// </summary>
    Task SaveAsync(TState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载状态
    /// </summary>
    Task<TState?> LoadAsync(CancellationToken cancellationToken = default);
}
