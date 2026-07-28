namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 管道上下文 — 贯穿所有 Fork 中间件
/// </summary>
public sealed class ForkContext
{
    /// <summary>
    /// 原始 Fork 选项
    /// </summary>
    public required ForkOptions Options { get; init; }

    /// <summary>
    /// 生成的 Fork ID（Manager 在管道执行前设置）
    /// </summary>
    public string ForkId { get; set; } = string.Empty;

    /// <summary>
    /// Fork 创建时间（Manager 在管道执行前设置）
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Fork 递归深度（Manager 在管道执行前预计算）
    /// </summary>
    public int ForkDepth { get; set; }

    /// <summary>
    /// 是否已验证通过（ForkValidationMiddleware 设置）
    /// </summary>
    public bool IsValidated { get; set; }

    /// <summary>
    /// 验证失败原因（ForkValidationMiddleware 设置）
    /// </summary>
    public string? ValidationFailureReason { get; set; }

    /// <summary>
    /// 共享缓存引用（Manager 在管道执行前设置）
    /// </summary>
    public Dictionary<string, string>? SharedCache { get; set; }

    /// <summary>
    /// Fork 指令消息（ForkSpawnMiddleware 设置）
    /// </summary>
    public string? ForkDirective { get; set; }

    /// <summary>
    /// CacheSafeParams 克隆副本（ForkSpawnMiddleware 设置）
    /// </summary>
    public CacheSafeParams? CacheSafeParams { get; set; }

    /// <summary>
    /// 构建后的 SubAgentOptions（ForkSpawnMiddleware 设置）
    /// </summary>
    public SubAgentOptions? AgentOptions { get; set; }

    /// <summary>
    /// Spawn 后的子智能体实例（ForkSpawnMiddleware 设置）
    /// </summary>
    public ISubAgent? Agent { get; set; }

    /// <summary>
    /// 权限是否已同步（ForkPermissionMiddleware 设置）
    /// </summary>
    public bool PermissionsSynced { get; set; }

    /// <summary>
    /// 执行结果（ForkExecutionMiddleware 设置）
    /// </summary>
    public SubAgentResult? ExecutionResult { get; set; }

    /// <summary>
    /// 最终 Fork 状态（ForkExecutionMiddleware 设置）
    /// </summary>
    public ForkState FinalState { get; set; } = ForkState.Failed;

    /// <summary>
    /// 最终结果文本（ForkExecutionMiddleware 设置）
    /// </summary>
    public string? FinalResult { get; set; }

    /// <summary>
    /// 是否为后台执行模式（ForkExecutionMiddleware 设置）
    /// </summary>
    public bool IsBackground { get; set; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }
}
