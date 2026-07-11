namespace JoinCode.Abstractions.State;

/// <summary>
/// 数据库路径解析工具
/// </summary>
public static class DatabasePathResolver
{
    /// <summary>
    /// 解析数据库路径。
    /// 默认使用 <see cref="AppDataConstants.Paths"/>.<see cref="AppDataPaths.JccDirectory"/> 作为基础路径
    /// （受 <c>JCC_APP_DATA_FOLDER</c> 环境变量控制），而非 <see cref="AppContext.BaseDirectory"/>。
    /// 修复 P2-2: 避免 SQLite DB 落在 exe 同目录导致多用户/多测试共享（历史泄漏）。
    /// </summary>
    /// <param name="configuredPath">配置的路径（可空）。若为 .json 后缀会自动转换为 .db。</param>
    /// <param name="defaultFileName">默认文件名，当 <paramref name="configuredPath"/> 为空时使用。</param>
    /// <returns>解析后的数据库文件绝对路径。</returns>
    public static string Resolve(string? configuredPath, string defaultFileName = "workflow_state.db")
    {
        // 优先使用 AppDataFolder（受 JCC_APP_DATA_FOLDER 控制），避免 DB 落在 exe 同目录
        // 详见 docs/AI交互文档/MockServer测试问题清单.md P2-2
        var basePath = AppDataConstants.Paths.JccDirectory;

        if (string.IsNullOrEmpty(configuredPath))
        {
            return Path.Combine(basePath, defaultFileName);
        }

        if (configuredPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            configuredPath = configuredPath[..^5] + ".db";
        }

        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.Combine(basePath, configuredPath);
    }
}

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
public sealed class ApiMessageDocument
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
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
