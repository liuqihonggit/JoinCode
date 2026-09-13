namespace JoinCode.Dream.Pipeline;

/// <summary>
/// Dream 提示构建中间件 — 构建系统提示与用户提示,用于 Dream 整合阶段
/// </summary>
[Register(typeof(IDreamMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DreamPromptBuildMiddleware : ServiceEntity, IDreamMiddleware
{
    /// <summary>
    /// 执行中间件 — 构建系统/用户提示并填充上下文,然后调用后续中间件
    /// </summary>
    /// <param name="ctx">Dream 管道上下文</param>
    /// <param name="next">后续中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步任务</returns>
    public Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct)
    {
        ctx.SystemPrompt = ConsolidationPrompt.BuildPrompt("memory/", "sessions/", ConsolidationPrompt.ToolConstraints);
        ctx.UserPrompt = ConsolidationPrompt.BuildExtraContext(ctx.SessionIds, ConsolidationPrompt.ToolConstraints);
        ctx.PromptBuilt = true;

        return next(ctx, ct);
    }
}
