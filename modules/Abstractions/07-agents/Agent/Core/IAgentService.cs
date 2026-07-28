namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代理服务接口
/// </summary>
public interface IAgentService
{
    /// <summary>
    /// 创建并启动代理
    /// </summary>
    Task<AgentInfo> SpawnAgentAsync(AgentSpawnOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// 等待代理完成（阻塞直到代理执行结束）
    /// </summary>
    Task<AgentResult> WaitForAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取代理信息
    /// </summary>
    Task<AgentInfo?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止代理
    /// </summary>
    Task<bool> StopAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取可用的代理类型
    /// </summary>
    Task<List<AgentTypeInfo>> GetAvailableAgentTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复已完成的代理 - 从 transcript 加载历史对话，继续执行
    /// </summary>
    Task<AgentInfo> ResumeAgentAsync(AgentResumeOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向运行中的代理发送消息
    /// </summary>
    Task<bool> SendMessageToAgentAsync(string agentId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向运行中的代理发送结构化消息 — 对齐 TS SendMessageTool 结构化消息路由
    /// </summary>
    Task<bool> SendStructuredMessageAsync(string agentId, StructuredMessageData structuredData, string rawMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取代理的待处理消息
    /// </summary>
    Task<IReadOnlyList<AgentMessageInfo>> GetAgentMessagesAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 代理完成事件（后台代理完成时触发）
    /// </summary>
    event EventHandler<AgentCompletedEventArgs>? AgentCompleted;

    /// <summary>
    /// 获取代理的进度信息
    /// </summary>
    Task<AgentProgress?> GetAgentProgressAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式运行子智能体 — 对齐 TS runAgent AsyncGenerator
    /// 返回 IAsyncEnumerable 实时输出子智能体的执行进度
    /// 前台模式：调用方通过 await foreach 实时消费
    /// </summary>
    IAsyncEnumerable<AgentStreamChunk> RunAgentStreamAsync(AgentSpawnOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// 代理协调器接口
/// </summary>
public interface IAgentCoordinator
{
    /// <summary>
    /// 停止代理
    /// </summary>
    Task<bool> StopAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有正在运行的代理
    /// </summary>
    Task<IReadOnlyList<RunningAgentInfo>> GetRunningAgentsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 代理完成事件参数
/// </summary>
public sealed class AgentCompletedEventArgs : EventArgs
{
    public required string AgentId { get; init; }
    public required AgentStatus Status { get; init; }
    public required string Description { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public long? ExecutionTimeMs { get; init; }
    public string? AgentType { get; init; }
    public string? ToolUseId { get; init; }
    public string? WorktreePath { get; init; }
    public string? WorktreeBranch { get; init; }
    public int? ToolUseCount { get; init; }
    public long? TokenCount { get; init; }
}

/// <summary>
/// 代理任务通知（注入LLM对话的结构化XML通知）
/// </summary>
public sealed class AgentTaskNotification
{
    public required string TaskId { get; init; }
    public required string Status { get; init; }
    public required string Description { get; init; }
    public string? ToolUseId { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public long? ExecutionTimeMs { get; init; }
    public string? AgentType { get; init; }
    public int? ToolUseCount { get; init; }
    public int? TokenCount { get; init; }
    public string? WorktreePath { get; init; }
    public string? WorktreeBranch { get; init; }

    public string ToXml()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<task-notification>");
        sb.Append("<task-id>").Append(TaskId).AppendLine("</task-id>");
        if (!string.IsNullOrEmpty(ToolUseId))
            sb.Append("<tool-use-id>").Append(ToolUseId).AppendLine("</tool-use-id>");
        sb.Append("<status>").Append(Status).AppendLine("</status>");
        sb.Append("<summary>Agent \"").Append(Description).Append("\" ").Append(Status).AppendLine("</summary>");
        if (!string.IsNullOrEmpty(Output))
        {
            sb.AppendLine("<result>");
            sb.AppendLine(Output);
            sb.AppendLine("</result>");
        }
        if (!string.IsNullOrEmpty(Error))
            sb.Append("<error>").Append(Error).AppendLine("</error>");
        sb.AppendLine("<usage>");
        if (TokenCount.HasValue)
            sb.Append("<total_tokens>").Append(TokenCount.Value).AppendLine("</total_tokens>");
        if (ToolUseCount.HasValue)
            sb.Append("<tool_uses>").Append(ToolUseCount.Value).AppendLine("</tool_uses>");
        if (ExecutionTimeMs.HasValue)
            sb.Append("<duration_ms>").Append(ExecutionTimeMs.Value).AppendLine("</duration_ms>");
        sb.AppendLine("</usage>");
        if (!string.IsNullOrEmpty(AgentType))
            sb.Append("<agent-type>").Append(AgentType).AppendLine("</agent-type>");
        if (!string.IsNullOrEmpty(WorktreePath))
        {
            sb.AppendLine("<worktree>");
            sb.Append("<worktreePath>").Append(WorktreePath).AppendLine("</worktreePath>");
            if (!string.IsNullOrEmpty(WorktreeBranch))
                sb.Append("<worktreeBranch>").Append(WorktreeBranch).AppendLine("</worktreeBranch>");
            sb.AppendLine("</worktree>");
        }
        sb.AppendLine("</task-notification>");
        return sb.ToString();
    }
}

/// <summary>
/// 代理信息
/// </summary>
public sealed record AgentInfo
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public string? AgentType { get; init; }
    public AgentStatus Status { get; init; } = AgentStatus.Pending;
    public AgentIsolationMode IsolationMode { get; init; } = AgentIsolationMode.None;
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Output { get; init; }
}

/// <summary>
/// 代理执行结果
/// </summary>
public sealed record AgentResult
{
    public required string AgentId { get; init; }
    public required bool Success { get; init; }
    public required string Output { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// 代理创建选项
/// </summary>
public sealed record AgentSpawnOptions
{
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public string? AgentType { get; init; }
    public bool RunInBackground { get; init; }
    public AgentIsolationMode IsolationMode { get; init; } = AgentIsolationMode.None;

    /// <summary>
    /// 记忆作用域 — 对齐 TS AgentTool InputSchema 的 memory 参数
    /// null 表示不启用记忆
    /// </summary>
    public AgentMemoryScope? MemoryScope { get; init; }

    /// <summary>
    /// 模型覆盖 — 对齐 TS AgentTool InputSchema 的 model 参数
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// 代理名称 — 对齐 TS AgentTool InputSchema 的 name 参数
    /// 用于 SendMessage 寻址
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 工作目录覆盖 — 对齐 TS AgentTool InputSchema 的 cwd 参数
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// 允许的工具列表 — 对齐 TS PromptCommand.allowedTools
    /// 技能 fork 模式下，限制子智能体只能使用指定工具
    /// 与 AgentDefinition.Tools 合并（调用方优先）
    /// </summary>
    public IReadOnlyList<string>? AllowedTools { get; init; }

    /// <summary>
    /// 推理努力级别 — 对齐 TS PromptCommand.effort
    /// 技能 fork 模式下，设置子智能体的推理努力级别
    /// </summary>
    public string? Effort { get; init; }
}

/// <summary>
/// 代理恢复选项 - 从已有 transcript 恢复代理执行
/// </summary>
public sealed record AgentResumeOptions
{
    public required string AgentId { get; init; }
    public required string NewPrompt { get; init; }
    public string? SessionId { get; init; }
    public bool RunInBackground { get; init; }
}

/// <summary>
/// 代理类型信息
/// </summary>
public sealed record AgentTypeInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public List<string>? AvailableTools { get; init; }
}

/// <summary>
/// 正在运行的代理信息
/// </summary>
public sealed record RunningAgentInfo
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public string? AgentType { get; init; }
    public DateTime? StartedAt { get; init; }
    public string? DisplayName { get; init; }
    public string? ColorHex { get; init; }
    public string? SpinnerVerb { get; init; }
    public AgentStatus State { get; init; }
    public long TokenCount { get; init; }
    public int ToolUseCount { get; init; }
}

/// <summary>
/// 代理隔离模式
/// </summary>
public enum AgentIsolationMode
{
    [EnumValue("none")] None,
    [EnumValue("worktree")] Worktree
}

/// <summary>
/// 代理消息信息
/// </summary>
public sealed record AgentMessageInfo
{
    public required string FromAgentId { get; init; }
    public required string MessageType { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed record ToolActivity
{
    public required string ToolName { get; init; }
    public string? ActivityDescription { get; init; }
    public bool IsSearch { get; init; }
    public bool IsRead { get; init; }
    public Dictionary<string, string>? Input { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed record AgentProgress
{
    public required int ToolUseCount { get; init; }
    public required int TokenCount { get; init; }
    public ToolActivity? LastActivity { get; init; }
    public IReadOnlyList<ToolActivity>? RecentActivities { get; init; }
    public string? Summary { get; init; }
}

public interface IProgressTracker
{
    void RecordToolUse(string toolName, string? activityDescription = null, Dictionary<string, string>? input = null);
    void RecordTokenUsage(int tokenCount);
    void UpdateSummary(string summary);
    AgentProgress ToProgress();
}
