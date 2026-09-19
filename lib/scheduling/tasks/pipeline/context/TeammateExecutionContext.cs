namespace Core.Scheduling.Tasks;


/// <summary>
/// 队友执行上下文 — 承载 In-Process 队友(子智能体)在管道中执行所需的全部状态与回调
/// </summary>
public sealed class TeammateExecutionContext : IPipelineContext {
    /// <summary>
    /// 队友定义 — 描述队友的静态配置(名称、角色、能力等)
    /// </summary>
    public required InProcessTeammateDefinition Definition { get; init; }

    /// <summary>
    /// 取消令牌 — 用于通知队友执行应中止
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// 执行起始时间(UTC)
    /// </summary>
    public DateTime StartTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 当前关联的智能体实例 — 执行过程中动态绑定
    /// </summary>
    public IAgent? Agent { get; set; }

    /// <summary>
    /// 队友运行时状态 — 跟踪队友生命周期进展
    /// </summary>
    public TeammateState? State { get; set; }

    /// <summary>
    /// 生命周期取消令牌源 — 用于从外部控制队友整体生命周期
    /// </summary>
    public CancellationTokenSource? LifecycleCts { get; set; }

    /// <summary>
    /// 是否已处理过持续模式 — 防止重复初始化标记
    /// </summary>
    public bool ContinuousModeHandled { get; set; }

    /// <summary>
    /// 队友执行结果 — 执行完成后填充
    /// </summary>
    public AgentTaskResult? Result { get; set; }

    /// <summary>
    /// 运行循环回调 — 定义队友主循环的执行逻辑
    /// </summary>
    public Func<InProcessTeammateDefinition, TeammateState, CancellationToken, Task>? RunLoopAsync { get; set; }

    /// <summary>
    /// 尝试清理回调 — 按队友名称进行轻量清理
    /// </summary>
    public Func<string, Task>? TryCleanupAsync { get; set; }

    /// <summary>
    /// 清理回调 — 按队友名称和状态进行完整清理
    /// </summary>
    public Func<string, TeammateState, Task>? CleanupAsync { get; set; }

    /// <summary>
    /// 活跃队友表 — 队友 ID 到其运行时状态的映射
    /// </summary>
    public ConcurrentDictionary<string, TeammateState> ActiveTeammates { get; set; } = new();

    /// <summary>
    /// 待处理消息表 — 队友 ID 到其消息通道的映射
    /// </summary>
    public ConcurrentDictionary<string, Channel<CoordinatorMessage>> PendingMessages { get; set; } = new();

    /// <inheritdoc/>
    bool IPipelineContext.Failed { get; set; }

    /// <inheritdoc/>
    string? IPipelineContext.ErrorMessage { get; set; }

    /// <inheritdoc/>
    void IPipelineContext.Fail(string message) {
        ((IPipelineContext)this).Failed = true;
        ((IPipelineContext)this).ErrorMessage = message;
    }
}