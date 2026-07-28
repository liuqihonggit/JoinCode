namespace Tools.Handlers;

/// <summary>
/// Agent 工具中间件共享上下文 — 在管道各阶段间传递状态
/// </summary>
public sealed class AgentToolContext
{
    // === 输入 ===

    /// <summary>
    /// 代理描述（3-5 个词）
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 任务提示词/指令
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// 代理类型（可选）
    /// </summary>
    public string? SubagentType { get; init; }

    /// <summary>
    /// 模型覆盖: sonnet/opus/haiku（可选）
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// 代理名称，用于 SendMessage 寻址（可选）
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 是否在后台运行
    /// </summary>
    public bool? RunInBackground { get; init; }

    /// <summary>
    /// 隔离模式: none/worktree（可选）
    /// </summary>
    public string? Isolation { get; init; }

    /// <summary>
    /// 工作目录覆盖（可选）
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// 记忆作用域: user/project/local（可选，启用代理记忆）
    /// </summary>
    public string? Memory { get; init; }

    // === AgentValidationMiddleware 填充 ===

    /// <summary>
    /// 验证错误信息 — 由 AgentValidationMiddleware 设置，非 null 表示验证失败
    /// </summary>
    public string? ValidationError { get; set; }

    // === AgentForkMiddleware 填充 ===

    /// <summary>
    /// Fork 结果 — 由 AgentForkMiddleware 设置，非 null 表示走了 fork 路径
    /// </summary>
    public ToolResult? ForkResult { get; set; }

    // === AgentBackgroundSpawnMiddleware 填充 ===

    /// <summary>
    /// 后台 Spawn 结果 — 由 AgentBackgroundSpawnMiddleware 设置，非 null 表示后台模式已启动
    /// </summary>
    public ToolResult? BackgroundSpawnResult { get; set; }

    /// <summary>
    /// Spawn 选项 — 由 AgentBackgroundSpawnMiddleware 设置，供后续中间件使用
    /// </summary>
    public AgentSpawnOptions? SpawnOptions { get; set; }

    // === AgentStreamExecutionMiddleware 填充 ===

    /// <summary>
    /// 代理 ID — 由流式执行中间件设置
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// 代理是否成功
    /// </summary>
    public bool Succeeded { get; set; } = true;

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long? ExecutionTimeMs { get; set; }

    /// <summary>
    /// 内容输出 — 由流式执行中间件累积
    /// </summary>
    public StringBuilder ContentBuilder { get; } = new();

    /// <summary>
    /// Worktree 路径 — agent 使用 worktree 隔离时设置
    /// 对齐 TS: worktreeInfo.worktreePath
    /// </summary>
    public string? WorktreePath { get; set; }

    /// <summary>
    /// Worktree 分支名 — agent 使用 worktree 隔离时设置
    /// 对齐 TS: worktreeInfo.worktreeBranch
    /// </summary>
    public string? WorktreeBranch { get; set; }

    // === AgentHandoffMiddleware 填充 ===

    /// <summary>
    /// Handoff 安全审查警告 — 由 AgentHandoffMiddleware 设置
    /// </summary>
    public string? HandoffWarning { get; set; }

    // === 最终结果 ===

    /// <summary>
    /// 最终结果 — 由管道末尾设置
    /// </summary>
    public ToolResult? Result { get; set; }
}
