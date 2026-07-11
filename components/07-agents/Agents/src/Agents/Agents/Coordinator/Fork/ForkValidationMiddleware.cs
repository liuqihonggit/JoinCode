namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 验证中间件 — 递归防护和深度限制检查
/// </summary>
[Register(typeof(IForkMiddleware))]
public sealed partial class ForkValidationMiddleware : IForkMiddleware
{
    [Inject] private readonly ILogger<ForkValidationMiddleware>? _logger;

    /// <summary>验证最先执行</summary>

    /// <summary>验证失败应中断管道</summary>
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(ForkContext context, MiddlewareDelegate<ForkContext> next, CancellationToken ct)
    {
        // 递归防护: 检查是否已在 fork 子代理上下文中
        if (context.Options.ParentMessageList is not null && ForkMessageBuilder.IsInForkChild(context.Options.ParentMessageList))
        {
            _logger?.LogWarning("Fork rejected: already in fork child context for parent {ParentSessionId}",
                context.Options.ParentSessionId);

            context.IsValidated = false;
            context.ValidationFailureReason = "Fork 递归防护: 当前已在 fork 子代理上下文中，禁止再次 fork";
            context.ForkId = $"fork-rejected-{Guid.NewGuid():N}";
            context.FinalState = ForkState.Failed;
            context.FinalResult = context.ValidationFailureReason;

            // 验证失败不调用 next，直接短路
            return Task.CompletedTask;
        }

        // 深度限制检查（ForkDepth 由 Manager 在管道执行前预计算）
        if (context.ForkDepth >= context.Options.MaxForkDepth)
        {
            _logger?.LogWarning("Fork depth limit reached: {Depth} >= {MaxDepth} for parent {ParentSessionId}",
                context.ForkDepth, context.Options.MaxForkDepth, context.Options.ParentSessionId);

            context.IsValidated = false;
            context.ValidationFailureReason = $"Fork 递归深度超限: 当前深度 {context.ForkDepth} 已达到最大深度 {context.Options.MaxForkDepth}";
            context.ForkId = $"fork-depth-exceeded-{Guid.NewGuid():N}";
            context.FinalState = ForkState.Failed;
            context.FinalResult = context.ValidationFailureReason;

            return Task.CompletedTask;
        }

        // 验证通过 — 使用 Manager 预生成的 ForkId
        context.IsValidated = true;

        return next(context, ct);
    }
}
