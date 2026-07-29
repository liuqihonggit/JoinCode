
namespace JoinCode.Abstractions.State;

/// <summary>
/// 应用全局状态定义
/// 参考 claude-code AppState 设计，包含所有需要响应式的状态
/// </summary>
public sealed record AppState
{
    /// <summary>
    /// 当前会话状态
    /// </summary>
    public SessionState Session { get; init; } = new();

    /// <summary>
    /// Agent 状态集合
    /// </summary>
    public ImmutableDictionary<string, AgentState> Agents { get; init; } = ImmutableDictionary<string, AgentState>.Empty;

    /// <summary>
    /// 任务状态集合
    /// </summary>
    public ImmutableDictionary<string, TaskState> Tasks { get; init; } = ImmutableDictionary<string, TaskState>.Empty;

    /// <summary>
    /// 配置状态
    /// </summary>
    public ConfigState Config { get; init; } = new();

    /// <summary>
    /// UI 状态
    /// </summary>
    public UiState Ui { get; init; } = new();

    /// <summary>
    /// MCP 服务器连接状态
    /// </summary>
    public McpState Mcp { get; init; } = new();

    /// <summary>
    /// Bridge 连接状态
    /// </summary>
    public BridgeState Bridge { get; init; } = new();

    /// <summary>
    /// 权限状态
    /// </summary>
    public PermissionState Permission { get; init; } = new();

    /// <summary>
    /// 获取默认状态
    /// </summary>
    public static AppState Default => new();
}

/// <summary>
/// 会话状态
/// </summary>
public sealed record SessionState
{
    /// <summary>
    /// 会话 ID
    /// </summary>
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>
    /// 聊天历史（不可变列表）
    /// </summary>
    public ImmutableList<ApiMessageState> MessageList { get; init; } = ImmutableList<ApiMessageState>.Empty;

    /// <summary>
    /// 会话开始时间
    /// </summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 当前模型名称
    /// </summary>
    public string? CurrentModel { get; init; }

    /// <summary>
    /// 是否处于计划模式
    /// </summary>
    public bool IsPlanMode { get; init; }

    /// <summary>
    /// 当前计划
    /// </summary>
    public string? CurrentPlan { get; init; }
}

/// <summary>
/// 聊天消息状态（用于序列化）
/// </summary>
public sealed record ApiMessageState
{
    /// <summary>
    /// 消息角色
    /// </summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 消息时间戳
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 消息元数据
    /// </summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } = ImmutableDictionary<string, string>.Empty;


}

/// <summary>
/// Agent 状态
/// </summary>
public sealed record AgentState
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>
    /// Agent 名称
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Agent 类型
    /// </summary>
    public string AgentType { get; init; } = string.Empty;

    /// <summary>
    /// 当前状态
    /// </summary>
    public AgentStatus Status { get; init; } = AgentStatus.Idle;

    /// <summary>
    /// 当前工作目录
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 当前任务 ID
    /// </summary>
    public string? CurrentTaskId { get; init; }

    /// <summary>
    /// Agent 元数据
    /// </summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Agent 状态枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 AgentStatusConstants + AgentStatusExtensions
/// 合并自 JoinCode.Abstractions.Interfaces.AgentStatus (Pending/Stopped) 和 JoinCode.Abstractions.State.AgentStatus (Idle/Paused)
/// </summary>
public enum AgentStatus
{
    /// <summary>等待启动</summary>
    [EnumValue("pending")] Pending = 0,

    /// <summary>空闲</summary>
    [EnumValue("idle")] Idle = 1,

    /// <summary>运行中</summary>
    [EnumValue("running")] Running = 2,

    /// <summary>已暂停</summary>
    [EnumValue("paused")] Paused = 3,

    /// <summary>已完成</summary>
    [EnumValue("completed")] Completed = 4,

    /// <summary>已失败</summary>
    [EnumValue("failed")] Failed = 5,

    /// <summary>已停止</summary>
    [EnumValue("stopped")] Stopped = 6
}

/// <summary>
/// Agent 状态扩展方法 — 提取自 AgentServiceImpl/SubAgent 等多处重复的终态/活跃态判断
/// </summary>
public static class AgentStatusHelper
{
    /// <summary>
    /// 是否处于终态（Completed/Failed/Stopped）— 不可再转换
    /// </summary>
    public static bool IsTerminal(this AgentStatus status)
        => status is AgentStatus.Completed or AgentStatus.Failed or AgentStatus.Stopped;

    /// <summary>
    /// 是否处于活跃态（Running/Pending/Idle）— 正在执行或即将执行
    /// </summary>
    public static bool IsActive(this AgentStatus status)
        => status is AgentStatus.Running or AgentStatus.Pending or AgentStatus.Idle;
}

/// <summary>
/// 任务状态
/// </summary>
public sealed record TaskState
{
    /// <summary>
    /// 任务 ID
    /// </summary>
    public string TaskId { get; init; } = string.Empty;

    /// <summary>
    /// 任务名称
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 任务描述
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 任务状态
    /// </summary>
    public TaskExecutionStatus Status { get; init; } = TaskExecutionStatus.Pending;

    /// <summary>
    /// 所属 Agent ID
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// 父任务 ID
    /// </summary>
    public string? ParentTaskId { get; init; }

    /// <summary>
    /// 子任务 ID 列表
    /// </summary>
    public ImmutableList<string> SubTaskIds { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// 进度百分比 (0-100)
    /// </summary>
    public int Progress { get; init; }

    /// <summary>
    /// 任务结果
    /// </summary>
    public string? Result { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// 任务元数据
    /// </summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } = ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// 任务执行状态
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 TaskExecutionStatusConstants + TaskExecutionStatusExtensions
/// 合并自: WorkflowState, AgentExecutionStatus, ShellBackgroundTaskStatus, AgentTaskStatus
/// </summary>
public enum TaskExecutionStatus
{
    /// <summary>等待执行</summary>
    [EnumValue("pending")] Pending = 0,

    /// <summary>执行中</summary>
    [EnumValue("running")] Running = 1,

    /// <summary>暂停</summary>
    [EnumValue("paused")] Paused = 2,

    /// <summary>已完成</summary>
    [EnumValue("completed")] Completed = 3,

    /// <summary>已失败</summary>
    [EnumValue("failed")] Failed = 4,

    /// <summary>已取消</summary>
    [EnumValue("cancelled")] Cancelled = 5,

    /// <summary>等待依赖</summary>
    [EnumValue("waitingForDependency")] WaitingForDependency = 6,

    /// <summary>就绪（依赖已满足，等待调度）</summary>
    [EnumValue("ready")] Ready = 7
}

/// <summary>
/// 任务执行状态扩展方法 — 提取自 AgentStateMachine/AgentCoordinator/TaskRuntime 等多处重复的终态/活跃态判断
/// </summary>
public static class TaskExecutionStatusHelper
{
    /// <summary>
    /// 是否处于终态（Completed/Failed/Cancelled）— 不可再转换（除重试外）
    /// </summary>
    public static bool IsTerminal(this TaskExecutionStatus status)
        => status is TaskExecutionStatus.Completed or TaskExecutionStatus.Failed or TaskExecutionStatus.Cancelled;

    /// <summary>
    /// 是否处于活跃态（Running/Ready/WaitingForDependency）— 正在执行或即将执行
    /// </summary>
    public static bool IsActive(this TaskExecutionStatus status)
        => status is TaskExecutionStatus.Running or TaskExecutionStatus.Ready or TaskExecutionStatus.WaitingForDependency;
}

/// <summary>
/// 配置状态
/// </summary>
public sealed record ConfigState
{
    /// <summary>
    /// 是否启用详细日志
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// 是否启用扩展思考
    /// 对齐 TS 版 AppState.thinkingEnabled
    /// </summary>
    public bool ThinkingEnabled { get; init; }

    /// <summary>
    /// 是否处于简洁模式
    /// </summary>
    public bool IsBriefMode { get; init; }

    /// <summary>
    /// 当前主题
    /// </summary>
    public string Theme { get; init; } = "default";

    /// <summary>
    /// 是否启用自动确认
    /// </summary>
    public bool AutoConfirm { get; init; }

    /// <summary>
    /// 最大令牌预算
    /// </summary>
    public long? MaxTokenBudget { get; init; }

    /// <summary>
    /// 已使用令牌数
    /// </summary>
    public long UsedTokens { get; init; }

    /// <summary>
    /// 配置项字典
    /// </summary>
    public ImmutableDictionary<string, string> Settings { get; init; } = ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// UI 状态
/// </summary>
public sealed record UiState
{
    /// <summary>
    /// 状态栏文本
    /// </summary>
    public string? StatusLineText { get; init; }

    /// <summary>
    /// 当前展开的视图
    /// </summary>
    public string ExpandedView { get; init; } = "none";

    /// <summary>
    /// 选中的 Agent 索引
    /// </summary>
    public int SelectedAgentIndex { get; init; } = -1;

    /// <summary>
    /// 选中的任务索引
    /// </summary>
    public int SelectedTaskIndex { get; init; } = -1;

    /// <summary>
    /// 当前视图选择模式
    /// </summary>
    public string ViewSelectionMode { get; init; } = "none";

    /// <summary>
    /// 是否显示加载动画
    /// </summary>
    public bool IsLoading { get; init; }

    /// <summary>
    /// 加载提示文本
    /// </summary>
    public string? SpinnerTip { get; init; }

    /// <summary>
    /// 当前通知消息
    /// </summary>
    public NotificationState? CurrentNotification { get; init; }

    /// <summary>
    /// 通知队列
    /// </summary>
    public ImmutableList<NotificationState> NotificationQueue { get; init; } = ImmutableList<NotificationState>.Empty;
}

/// <summary>
/// 通知状态
/// </summary>
public sealed record NotificationState
{
    /// <summary>
    /// 通知 ID
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 通知类型
    /// </summary>
    public NotificationType Type { get; init; } = NotificationType.Info;

    /// <summary>
    /// 通知标题
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 通知内容
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; init; }
}

/// <summary>
/// 通知类型
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 NotificationTypeConstants + NotificationTypeExtensions
/// </summary>
public enum NotificationType
{
    /// <summary>信息</summary>
    [EnumValue("info")] Info = 0,

    /// <summary>成功</summary>
    [EnumValue("success")] Success = 1,

    /// <summary>警告</summary>
    [EnumValue("warning")] Warning = 2,

    /// <summary>错误</summary>
    [EnumValue("error")] Error = 3
}

/// <summary>
/// MCP 状态
/// </summary>
public sealed record McpState
{
    /// <summary>
    /// 已连接的 MCP 服务器
    /// </summary>
    public ImmutableList<McpServerState> Servers { get; init; } = ImmutableList<McpServerState>.Empty;

    /// <summary>
    /// 可用工具列表
    /// </summary>
    public ImmutableList<string> AvailableTools { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// 可用资源列表
    /// </summary>
    public ImmutableDictionary<string, ImmutableList<string>> AvailableResources { get; init; } = ImmutableDictionary<string, ImmutableList<string>>.Empty;

    /// <summary>
    /// 插件重连密钥（用于触发重新连接）
    /// </summary>
    public int PluginReconnectKey { get; init; }
}

/// <summary>
/// MCP 服务器状态
/// </summary>
public sealed record McpServerState
{
    /// <summary>
    /// 服务器名称
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 服务器 ID
    /// </summary>
    public string ServerId { get; init; } = string.Empty;

    /// <summary>
    /// 连接状态
    /// </summary>
    public McpConnectionStatus Status { get; init; } = McpConnectionStatus.Disconnected;

    /// <summary>
    /// 最后错误信息
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// 连接时间
    /// </summary>
    public DateTime? ConnectedAt { get; init; }
}

/// <summary>
/// MCP 连接状态
/// </summary>
public enum McpConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// Bridge 状态
/// </summary>
public sealed record BridgeState
{
    /// <summary>
    /// 是否启用 Bridge
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected { get; init; }

    /// <summary>
    /// 是否正在重连
    /// </summary>
    public bool IsReconnecting { get; init; }

    /// <summary>
    /// 连接 URL
    /// </summary>
    public string? ConnectUrl { get; init; }

    /// <summary>
    /// 会话 URL
    /// </summary>
    public string? SessionUrl { get; init; }

    /// <summary>
    /// 环境 ID
    /// </summary>
    public string? EnvironmentId { get; init; }

    /// <summary>
    /// 会话 ID
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// 最后错误信息
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// 是否仅出站模式
    /// </summary>
    public bool IsOutboundOnly { get; init; }
}

/// <summary>
/// 权限状态
/// </summary>
public sealed record PermissionState
{
    /// <summary>
    /// 当前权限模式
    /// </summary>
    public PermissionMode PermissionMode { get; init; } = PermissionMode.Default;

    /// <summary>
    /// 进入 Plan 模式前保存的权限模式，退出时恢复
    /// 对齐 TS ToolPermissionContext.prePlanMode
    /// </summary>
    public PermissionMode? PrePlanMode { get; init; }

    /// <summary>
    /// 自动批准的工具列表
    /// </summary>
    public ImmutableList<string> AutoApprovedTools { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// 自动拒绝的工具列表
    /// </summary>
    public ImmutableList<string> AutoRejectedTools { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// 待处理的权限请求
    /// </summary>
    public ImmutableList<PermissionRequestState> PendingRequests { get; init; } = ImmutableList<PermissionRequestState>.Empty;

    /// <summary>
    /// 是否可用绕过权限模式
    /// </summary>
    public bool IsBypassPermissionsModeAvailable { get; init; }
}

/// <summary>
/// 权限请求状态
/// </summary>
public sealed record PermissionRequestState
{
    /// <summary>
    /// 请求 ID
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; init; } = string.Empty;

    /// <summary>
    /// 请求描述
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 请求状态
    /// </summary>
    public PermissionRequestStatus Status { get; init; } = PermissionRequestStatus.Pending;
}

/// <summary>
/// 权限请求状态枚举
/// </summary>
public enum PermissionRequestStatus
{
    [EnumValue("pending")] Pending,
    [EnumValue("approved")] Approved,
    [EnumValue("rejected")] Rejected,
    [EnumValue("expired")] Expired
}
