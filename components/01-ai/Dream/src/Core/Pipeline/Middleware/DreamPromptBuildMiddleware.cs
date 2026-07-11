namespace JoinCode.Dream.Pipeline;

[Register]
public sealed partial class DreamPromptBuildMiddleware : IDreamMiddleware
{
    public Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct)
    {
        ctx.SystemPrompt = ConsolidationPrompt.BuildPrompt("memory/", "sessions/", ConsolidationPrompt.ToolConstraints);
        ctx.UserPrompt = ConsolidationPrompt.BuildExtraContext(ctx.SessionIds, ConsolidationPrompt.ToolConstraints);
        ctx.PromptBuilt = true;

        return next(ctx, ct);
    }
}
