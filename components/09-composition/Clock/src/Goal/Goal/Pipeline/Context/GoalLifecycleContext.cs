namespace Core.Goal;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 目标生命周期操作类型
/// </summary>
public enum GoalOperation
{
    Start,
    Pause,
    Resume,
    Clear,
    MarkCompleted,
    MarkUnmet
}

/// <summary>
/// 目标生命周期管道共享上下文 — 在中间件各阶段间传递状态
/// </summary>
public sealed class GoalLifecycleContext : PipelineContextBase
{
    // === 输入 ===

    /// <summary>操作类型</summary>
    public required GoalOperation Operation { get; init; }

    /// <summary>目标描述（仅 Start 需要）</summary>
    public string? Objective { get; init; }

    /// <summary>约束条件（仅 Start 需要）</summary>
    public List<string>? Constraints { get; init; }

    /// <summary>Token 预算（仅 Start 需要）</summary>
    public int? TokenBudget { get; init; }

    /// <summary>评估原因（仅 MarkCompleted/MarkUnmet 需要）</summary>
    public string? Reason { get; init; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    // === 服务依赖 ===

    /// <summary>目标状态</summary>
    public required GoalState State { get; set; }

    /// <summary>聊天历史</summary>
    public required MessageList ChatHistory { get; init; }

    /// <summary>心跳</summary>
    public required IGoalHeartbeat Heartbeat { get; init; }

    /// <summary>权限管理器</summary>
    public IToolPermissionManager? PermissionManager { get; init; }

    // === Step 2: StateTransitionMiddleware 填充 ===

    /// <summary>状态变更是否已执行</summary>
    public bool StateTransitioned { get; set; }

    /// <summary>保存的权限模式（Start 时保存，Clear/MarkCompleted/MarkUnmet 时恢复）</summary>
    public PermissionMode? SavedPermissionMode { get; set; }

    // === Step 4: EngineControlMiddleware 填充 ===

    /// <summary>是否需要启动引擎循环</summary>
    public bool ShouldStartEngineLoop { get; set; }

    /// <summary>是否需要取消引擎循环</summary>
    public bool ShouldCancelEngineLoop { get; set; }

    // === Step 5: HeartbeatControlMiddleware 填充 ===

    /// <summary>是否需要重置心跳</summary>
    public bool ShouldResetHeartbeat { get; set; }

    // === Step 6: CompletionSignalMiddleware 填充 ===

    /// <summary>是否需要设置完成信号</summary>
    public bool ShouldSignalCompletion { get; set; }

    // === 输出 ===

    /// <summary>最终目标状态</summary>
    public GoalState? Result { get; set; }
}
