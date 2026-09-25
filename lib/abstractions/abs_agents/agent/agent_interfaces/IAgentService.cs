namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代理服务接口
/// </summary>
public interface IAgentService {
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
    /// 将用户输入转发给运行中的子代理 — 用户在子代理运行期间追加的输入
    /// 与 SendMessageToAgentAsync 区别：消息入 IAgentInputForwardQueue，由子代理每轮 LLM 调用前主动消费
    /// </summary>
    Task<bool> ForwardUserInputToAgentAsync(string agentId, string userInput, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向运行中的代理发送结构化消息 — 对齐 TS SendMessageTool 结构化消息路由
    /// </summary>
    Task<bool> SendStructuredMessageAsync(string agentId, StructuredMessageData structuredData, string rawMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取代理的待处理消息
    /// </summary>
    Task<IEnumerable<AgentMessageInfo>> GetAgentMessagesAsync(string agentId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// 获取所有正在运行的代理
    /// </summary>
    Task<IEnumerable<RunningAgentInfo>> GetRunningAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 ID 获取运行中的代理 — O(1) 字典查找，避免 GetRunningAgentsAsync + FirstOrDefault 的 O(n) 线性检索
    /// </summary>
    /// <param name="agentId">代理 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>运行中的代理信息；不存在或非运行状态返回 null</returns>
    Task<RunningAgentInfo?> GetRunningAgentByIdAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按名称查找运行中子代理的 ID — O(1) 字典查找
    /// 匹配键: DisplayName → Name → Description → Id（均精确匹配，大小写不敏感）
    /// 几百个子代理场景下用 map 替代遍历，路由性能 O(1)
    /// </summary>
    /// <returns>匹配的 agentId，未找到返回 null</returns>
    Task<string?> FindAgentIdByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定子代理的 worktree 隔离目录 — 供 GUI 右键直达资源管理器。
    /// 未启用 worktree 或代理不存在返回 null。
    /// </summary>
    Task<string?> GetAgentWorktreePathAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}

/// <summary>
/// 代理完成事件参数
/// </summary>
public sealed class AgentCompletedEventArgs : EventArgs {
    /// <summary>获取代理标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取代理完成状态。</summary>
    public required AgentStatus Status { get; init; }
    /// <summary>获取代理描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取代理输出。</summary>
    public string? Output { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? Error { get; init; }
    /// <summary>获取执行耗时（毫秒）。</summary>
    public long? ExecutionTimeMs { get; init; }
    /// <summary>获取代理角色。</summary>
    public AgentRole Role { get; init; }
    /// <summary>获取执行器变体。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>获取工具使用标识。</summary>
    public string? ToolUseId { get; init; }
    /// <summary>获取工作树路径。</summary>
    public string? WorktreePath { get; init; }
    /// <summary>获取工作树分支。</summary>
    public string? WorktreeBranch { get; init; }
    /// <summary>获取工具使用次数。</summary>
    public int? ToolUseCount { get; init; }
    /// <summary>获取 Token 总数。</summary>
    public long? TokenCount { get; init; }
}

/// <summary>
/// 代理任务通知（注入LLM对话的结构化XML通知）
/// </summary>
public sealed class AgentTaskNotification {
    /// <summary>获取任务标识。</summary>
    public required string TaskId { get; init; }
    /// <summary>获取任务状态。</summary>
    public required string Status { get; init; }
    /// <summary>获取任务描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取工具使用标识。</summary>
    public string? ToolUseId { get; init; }
    /// <summary>获取任务输出。</summary>
    public string? Output { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? Error { get; init; }
    /// <summary>获取执行耗时（毫秒）。</summary>
    public long? ExecutionTimeMs { get; init; }
    /// <summary>获取代理角色。</summary>
    public AgentRole Role { get; init; }
    /// <summary>获取执行器变体。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>获取工具使用次数。</summary>
    public int? ToolUseCount { get; init; }
    /// <summary>获取 Token 总数。</summary>
    public int? TokenCount { get; init; }
    /// <summary>获取工作树路径。</summary>
    public string? WorktreePath { get; init; }
    /// <summary>获取工作树分支。</summary>
    public string? WorktreeBranch { get; init; }

    /// <summary>生成任务通知的 XML 表示。</summary>
    public string ToXml() {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<task-notification>");
        sb.Append("<task-id>").Append(TaskId).AppendLine("</task-id>");
        if (!string.IsNullOrEmpty(ToolUseId))
            sb.Append("<tool-use-id>").Append(ToolUseId).AppendLine("</tool-use-id>");
        sb.Append("<status>").Append(Status).AppendLine("</status>");
        sb.Append("<summary>Agent \"").Append(Description).Append("\" ").Append(Status).AppendLine("</summary>");
        if (!string.IsNullOrEmpty(Output)) {
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
        if (Variant.HasValue)
            sb.Append("<agent-type>").Append(Role.ToValue()).Append(":").Append(Variant.Value.ToValue()).AppendLine("</agent-type>");
        else
            sb.Append("<agent-type>").Append(Role.ToValue()).AppendLine("</agent-type>");
        if (!string.IsNullOrEmpty(WorktreePath)) {
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
public sealed record AgentInfo {
    /// <summary>获取代理标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取代理描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取代理角色。</summary>
    public AgentRole Role { get; init; }
    /// <summary>获取执行器变体。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>获取或设置代理状态。</summary>
    public AgentStatus Status { get; init; } = AgentStatus.Pending;
    /// <summary>获取隔离模式。</summary>
    public AgentIsolationMode IsolationMode { get; init; } = AgentIsolationMode.None;
    /// <summary>获取启动时间。</summary>
    public DateTime? StartedAt { get; init; }
    /// <summary>获取完成时间。</summary>
    public DateTime? CompletedAt { get; init; }
    /// <summary>获取代理输出。</summary>
    public string? Output { get; init; }
}

/// <summary>
/// 代理执行结果
/// </summary>
public sealed record AgentResult {
    /// <summary>获取代理标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取是否成功。</summary>
    public required bool Success { get; init; }
    /// <summary>获取代理输出。</summary>
    public required string Output { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 代理创建选项
/// </summary>
public sealed record AgentSpawnOptions {
    /// <summary>获取代理描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取代理提示词。</summary>
    public required string Prompt { get; init; }
    /// <summary>获取代理角色。</summary>
    public AgentRole Role { get; init; } = AgentRole.Executor;
    /// <summary>获取执行器变体。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>获取是否在后台运行。</summary>
    public bool RunInBackground { get; init; }
    /// <summary>获取隔离模式。</summary>
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
    public IEnumerable<string>? AllowedTools { get; init; }

    /// <summary>
    /// 推理努力级别 — 对齐 TS PromptCommand.effort
    /// 技能 fork 模式下，设置子智能体的推理努力级别
    /// </summary>
    public string? Effort { get; init; }

    /// <summary>
    /// Goal 绑定标识 — 该 Agent 服务于哪个 Goal
    /// </summary>
    public string? GoalId { get; init; }

    /// <summary>
    /// Graph 节点绑定标识 — 该 Agent 绑定到 Goal Graph 的哪个节点
    /// </summary>
    public string? GraphNodeId { get; init; }

    /// <summary>
    /// Token 预算 — 限制 Agent 的 Token 消耗
    /// </summary>
    public int? TokenBudget { get; init; }

    /// <summary>
    /// 是否使用全新上下文 — 不继承父 Agent 的 ChatHistory
    /// </summary>
    public bool FreshContext { get; init; }

    /// <summary>
    /// 系统提示词覆盖
    /// </summary>
    public string? SystemPrompt { get; init; }
}

/// <summary>
/// 代理恢复选项 - 从已有 transcript 恢复代理执行
/// </summary>
public sealed record AgentResumeOptions {
    /// <summary>获取代理标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取新提示词。</summary>
    public required string NewPrompt { get; init; }
    /// <summary>获取会话标识。</summary>
    public string? SessionId { get; init; }
    /// <summary>获取是否在后台运行。</summary>
    public bool RunInBackground { get; init; }
}

/// <summary>
/// 代理类型信息
/// </summary>
public sealed record AgentTypeInfo {
    /// <summary>获取代理类型名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取代理类型描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取可用工具列表。</summary>
    public List<string>? AvailableTools { get; init; }
}

/// <summary>
/// 正在运行的代理信息
/// </summary>
public sealed record RunningAgentInfo {
    /// <summary>获取代理标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取代理描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取代理角色。</summary>
    public AgentRole Role { get; init; }
    /// <summary>获取执行器变体。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>获取启动时间。</summary>
    public DateTime? StartedAt { get; init; }
    /// <summary>获取显示名称。</summary>
    public string? DisplayName { get; init; }
    /// <summary>获取颜色十六进制值。</summary>
    public string? ColorHex { get; init; }
    /// <summary>获取旋转动词。</summary>
    public string? SpinnerVerb { get; init; }
    /// <summary>获取代理状态。</summary>
    public AgentStatus State { get; init; }
    /// <summary>获取 Token 总数。</summary>
    public long TokenCount { get; init; }
    /// <summary>获取工具使用次数。</summary>
    public int ToolUseCount { get; init; }
}

/// <summary>
/// 代理隔离模式
/// </summary>
public enum AgentIsolationMode {
    [EnumValue("none")] None,
    [EnumValue("worktree")] Worktree,
    [EnumValue("remote")] Remote
}

/// <summary>
/// 代理消息信息
/// </summary>
public sealed record AgentMessageInfo {
    /// <summary>获取来源代理标识。</summary>
    public required string FromAgentId { get; init; }
    /// <summary>获取消息类型。</summary>
    public required string MessageType { get; init; }
    /// <summary>获取消息内容。</summary>
    public required string Content { get; init; }
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed record ToolActivity {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取活动描述。</summary>
    public string? ActivityDescription { get; init; }
    /// <summary>获取是否为搜索活动。</summary>
    public bool IsSearch { get; init; }
    /// <summary>获取是否为读取活动。</summary>
    public bool IsRead { get; init; }
    /// <summary>获取工具输入参数。</summary>
    public Dictionary<string, string>? Input { get; init; }
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed record AgentProgress {
    /// <summary>获取工具使用次数。</summary>
    public required int ToolUseCount { get; init; }
    /// <summary>获取 Token 总数。</summary>
    public required int TokenCount { get; init; }
    /// <summary>获取最近一次工具活动。</summary>
    public ToolActivity? LastActivity { get; init; }
    /// <summary>获取最近活动集合。</summary>
    public IEnumerable<ToolActivity>? RecentActivities { get; init; }
    /// <summary>获取进度摘要。</summary>
    public string? Summary { get; init; }
}

public interface IProgressTracker {
    /// <summary>记录工具使用。</summary>
    void RecordToolUse(string toolName, string? activityDescription = null, Dictionary<string, string>? input = null);
    /// <summary>记录 Token 用量。</summary>
    void RecordTokenUsage(int tokenCount);
    /// <summary>更新进度摘要。</summary>
    void UpdateSummary(string summary);
    /// <summary>转换为代理进度信息。</summary>
    AgentProgress ToProgress();
}