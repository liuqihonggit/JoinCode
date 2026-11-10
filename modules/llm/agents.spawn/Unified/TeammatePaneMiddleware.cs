namespace Core.Agents;

/// <summary>
/// Teammate Pane 创建中间件 — 创建 Teammate UI Pane
/// 合并自路径 B 的 SpawnCoordTeammatePaneMiddleware
/// 主代理 no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammatePaneMiddleware : ServiceEntity, IUnifiedSpawnMiddleware {

    /// <summary>
    /// 构造 TeammatePaneMiddleware 实例，注入子代理上下文访问器、日志器及可选的布局管理器
    /// </summary>
    public TeammatePaneMiddleware(ISubAgentContextAccessor subAgentContextAccessor, ILogger<TeammatePaneMiddleware> logger, ITeammateLayoutManager? layoutManager = null) {
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
        _layoutManager = layoutManager;
    }
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ITeammateLayoutManager? _layoutManager;
    private readonly ILogger<TeammatePaneMiddleware> _logger;

    /// <summary>中间件错误处理策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行 Teammate Pane 创建：非主代理且布局管理器可用时创建 UI Pane
    /// </summary>
    /// <param name="context">统一 Spawn 上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct) {
        // 角色守卫（TASK062 P5）— 仅 teammate 变体创建 UI Pane；executor 等普通前台子代理
        // 无 pane 语义（Windows 无 tmux 场景曾对每个 executor spawn 打警告噪音 — E2E 实测）
        var current = _subAgentContextAccessor.Current;
        var isTeammate = current?.Variant == ExecutorVariant.Teammate;
        if (!context.IsMainAgent && isTeammate && _layoutManager is not null && context.Agent is not null) {
            try {
                var agentType = current?.Variant?.ToValue() ?? current?.Role.ToValue() ?? "agent";
                var command = $"# Agent: {context.Task}";
                await _layoutManager.CreateTeammatePaneAsync(context.AgentId, agentType, command, context.CancellationToken).ConfigureAwait(false);
                context.TeammatePaneCreated = true;
            } catch (Exception ex) {
                _logger.LogWarning(ex, "[TeammatePane] 创建 Teammate {AgentId} Pane 失败", context.AgentId);
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}