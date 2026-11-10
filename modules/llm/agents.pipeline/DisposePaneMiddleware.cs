namespace Core.Agents.Coordinator;

/// <summary>
/// 面板释放中间件 — 在 Agent 释放管道中移除队友对应的终端面板
/// </summary>
[Register(typeof(IAgentDisposeMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DisposePaneMiddleware : ServiceEntity, IAgentDisposeMiddleware {

    /// <summary>
    /// 构造面板释放中间件实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="layoutManager">可选队友布局管理器，缺省时跳过面板移除</param>
    public DisposePaneMiddleware(ILogger<DisposePaneMiddleware> logger, ITeammateLayoutManager? layoutManager = null) {
        _logger = logger;
        _layoutManager = layoutManager;
    }
    private readonly ITeammateLayoutManager? _layoutManager;
    private readonly ILogger<DisposePaneMiddleware> _logger;

    /// <summary>
    /// 执行中间件逻辑：移除指定 Agent 的终端面板后继续管道
    /// </summary>
    /// <param name="ctx">Agent 释放上下文</param>
    /// <param name="next">管道下一步委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct) {
        if (_layoutManager is not null) {
            try {
                await _layoutManager.RemoveTeammatePaneAsync(ctx.AgentId, ctx.CancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "[AgentCoordinator] 移除 Teammate {AgentId} Pane 失败", ctx.AgentId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}