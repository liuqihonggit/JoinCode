
namespace Core.Bridge.Init.V1;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V1 创建会话 — 对齐 TS 端: createSession
/// </summary>
[Register]
internal sealed class V1SessionCreationMiddleware : IMiddleware<V1BridgeInitContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(V1BridgeInitContext ctx, MiddlewareDelegate<V1BridgeInitContext> next, CancellationToken ct)
    {
        var sessionId = await ctx.Parameters.CreateSession(
            ctx.EnvironmentId!, ctx.Parameters.Title, ctx.Parameters.GitRepoUrl, ctx.AccessToken!, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(sessionId))
        {
            ctx.Fail("Session creation returned empty");
            return;
        }

        ctx.SessionId = sessionId;
        ctx.Logger?.LogInformation("Bridge v1: 会话已创建: {SessionId}", sessionId);
        await next(ctx, ct).ConfigureAwait(false);
    }
}
