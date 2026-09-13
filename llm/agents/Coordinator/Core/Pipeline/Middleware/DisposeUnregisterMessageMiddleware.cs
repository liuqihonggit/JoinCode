namespace Core.Agents.Coordinator;

/// <summary>
/// 消息注销中间件 — 在 Agent 释放管道中从消息邮箱注销该 Agent 的消息通道
/// </summary>
[Register(typeof(IAgentDisposeMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DisposeUnregisterMessageMiddleware : ServiceEntity, IAgentDisposeMiddleware
{

    /// <summary>
    /// 构造消息注销中间件实例
    /// </summary>
    /// <param name="messageBroker">进程内消息邮箱</param>
    /// <param name="logger">日志记录器</param>
    public DisposeUnregisterMessageMiddleware(IMailbox messageBroker, ILogger<DisposeUnregisterMessageMiddleware> logger)
    {
        _messageBroker = messageBroker;
        _logger = logger;
    }
    private readonly IMailbox _messageBroker;
    private readonly ILogger<DisposeUnregisterMessageMiddleware> _logger;

    /// <summary>
    /// 执行中间件逻辑：注销指定 Agent 的消息通道后继续管道
    /// </summary>
    /// <param name="ctx">Agent 释放上下文</param>
    /// <param name="next">管道下一步委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct)
    {
        try
        {
            _messageBroker.UnregisterAgent(ctx.AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AgentCoordinator] 注销Agent {AgentId} 消息通道时发生异常", ctx.AgentId);
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
