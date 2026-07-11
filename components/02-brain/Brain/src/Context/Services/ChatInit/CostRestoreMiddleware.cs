using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 成本恢复中间件 — 从持久化存储恢复会话成本状态
/// </summary>
[Register(typeof(IChatInitMiddleware))]
public sealed partial class CostRestoreMiddleware : IChatInitMiddleware
{
    private readonly ISessionCostPersistence? _sessionCostPersistence;
    private readonly ISessionStats _sessionStats;
    [Inject] private readonly ILogger<CostRestoreMiddleware>? _logger;

    /// <summary>
    /// 初始化成本恢复中间件
    /// </summary>
    public CostRestoreMiddleware(
        ISessionStats sessionStats,
        ISessionCostPersistence? sessionCostPersistence = null,
        ILogger<CostRestoreMiddleware>? logger = null)
    {
        _sessionCostPersistence = sessionCostPersistence;
        _sessionStats = sessionStats;
        _logger = logger;
    }

    /// <summary>成本恢复在上下文加载之后</summary>

    /// <summary>成本恢复失败不应中断管道</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 从持久化存储恢复会话成本状态
    /// </summary>
    public async Task InvokeAsync(ChatInitContext context, MiddlewareDelegate<ChatInitContext> next, CancellationToken ct)
    {
        if (_sessionCostPersistence is not null)
        {
            var sessionId = context.SessionId;
            var restoredStats = await _sessionCostPersistence.RestoreCostStateForSessionAsync(sessionId, ct).ConfigureAwait(false);
            if (restoredStats is not null)
            {
                _sessionStats.SeedCarryover(
                    restoredStats.CacheReadTokens,
                    restoredStats.CacheCreationTokens,
                    restoredStats.TotalCostUsd);
                _logger?.LogInformation("[ChatInit] 已恢复会话 {SessionId} 的成本状态", sessionId);
            }

            // 传递给 ChatInitializer 供 SaveCurrentCostsAsync 使用
            context.SessionCostPersistence = _sessionCostPersistence;
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
