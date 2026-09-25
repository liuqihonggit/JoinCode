namespace JoinCode.Abstractions.Interfaces;

public interface IForkSubAgentManager {
    /// <summary>分叉子智能体异步执行指定任务。</summary>
    Task<ForkResult> ForkAsync(ForkOptions options, CancellationToken ct = default);

    /// <summary>获取所有活跃分叉子智能体列表。</summary>
    Task<IReadOnlyList<ForkSubAgent>> GetActiveForksAsync(CancellationToken ct = default);

    /// <summary>合并指定分叉子智能体的结果。</summary>
    Task<ForkResult> MergeForkAsync(string forkId, CancellationToken ct = default);

    /// <summary>取消指定分叉子智能体。</summary>
    Task CancelForkAsync(string forkId, CancellationToken ct = default);

    /// <summary>分叉完成事件。</summary>
    event EventHandler<ForkCompletedEventArgs>? ForkCompleted;
}

public sealed class ForkOptions {
    /// <summary>获取父会话标识。</summary>
    public required string ParentSessionId { get; init; }
    /// <summary>获取任务描述。</summary>
    public required string TaskDescription { get; init; }
    /// <summary>获取或设置是否共享缓存。</summary>
    public bool ShareCache { get; init; } = true;
    /// <summary>获取或设置是否共享上下文。</summary>
    public bool ShareContext { get; init; } = true;
    /// <summary>获取或设置系统提示词。</summary>
    public string? SystemPrompt { get; init; }
    /// <summary>获取或设置最大迭代次数。</summary>
    public int MaxIterations { get; init; } = 10;
    /// <summary>获取或设置权限模式。</summary>
    public JoinCode.Abstractions.Security.PermissionMode PermissionMode { get; init; } = JoinCode.Abstractions.Security.PermissionMode.Plan;
    /// <summary>获取或设置允许的工具列表。</summary>
    public List<string> AllowedTools { get; init; } = [];
    /// <summary>获取或设置禁用的工具列表。</summary>
    public List<string> DeniedTools { get; init; } = [];
    /// <summary>获取或设置是否在后台运行。</summary>
    public bool RunInBackground { get; init; }
    /// <summary>获取或设置最大分叉深度。</summary>
    public int MaxForkDepth { get; init; } = 3;
    /// <summary>获取或设置父消息列表。</summary>
    public MessageList? ParentMessageList { get; init; }
    /// <summary>获取或设置缓存安全参数。</summary>
    public CacheSafeParams? CacheSafeParams { get; init; }
    /// <summary>获取或设置是否使用精确工具集。</summary>
    public bool UseExactTools { get; init; } = true;

    /// <summary>
    /// 隔离模式 — 对齐 TS AgentTool isolation 参数
    /// Fork 路径下指定 worktree 隔离，让子智能体在独立工作树中执行
    /// </summary>
    public AgentIsolationMode IsolationMode { get; init; } = AgentIsolationMode.None;

    /// <summary>
    /// 子代理事件通道 — 调用方（AgentForkMiddleware）从环境态捕获后传入，
    /// 供后台 fork 完成时发射 <see cref="ChatStreamEventType.AgentFinished"/> 终态事件。
    /// fork 无流式 chunk，故只有生命周期两端事件；回合结束后到达的事件由死通道自然丢弃。
    /// </summary>
    public JoinCode.Abstractions.LLM.Chat.SubAgentEventChannel? EventChannel { get; init; }
}

public sealed class ForkSubAgent {
    /// <summary>获取分叉标识。</summary>
    public required string ForkId { get; init; }
    /// <summary>获取父会话标识。</summary>
    public required string ParentSessionId { get; init; }
    /// <summary>获取分叉状态。</summary>
    public required ForkState State { get; init; }
    /// <summary>获取创建时间。</summary>
    public required DateTime CreatedAt { get; init; }
    /// <summary>获取结果。</summary>
    public string? Result { get; init; }
}

public enum ForkState {
    [EnumValue("running")] Running,
    [EnumValue("completed")] Completed,
    [EnumValue("merged")] Merged,
    [EnumValue("cancelled")] Cancelled,
    [EnumValue("failed")] Failed
}

public sealed class ForkResult {
    /// <summary>获取分叉标识。</summary>
    public required string ForkId { get; init; }
    /// <summary>获取分叉状态。</summary>
    public required ForkState State { get; init; }
    /// <summary>获取结果。</summary>
    public string? Result { get; init; }
    /// <summary>获取共享缓存。</summary>
    public Dictionary<string, string> SharedCache { get; init; } = new();
}

public sealed class ForkCompletedEventArgs : EventArgs {
    /// <summary>获取分叉标识。</summary>
    public required string ForkId { get; init; }
    /// <summary>获取分叉状态。</summary>
    public required ForkState State { get; init; }
    /// <summary>获取任务描述。</summary>
    public required string TaskDescription { get; init; }
    /// <summary>获取结果。</summary>
    public string? Result { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? Error { get; init; }
    /// <summary>获取工作树路径。</summary>
    public string? WorktreePath { get; init; }
}