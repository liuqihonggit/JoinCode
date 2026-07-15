
namespace Core.Bridge.Init.V1;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V1 Perpetual 模式: 验证已有会话是否存活 — 对齐 TS 端: getBridgeSession
/// best-effort: 验证失败不阻塞主流程
/// </summary>
[Register]
internal sealed partial class V1PerpetualSessionValidationMiddleware : IMiddleware<V1BridgeInitContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(V1BridgeInitContext ctx, MiddlewareDelegate<V1BridgeInitContext> next, CancellationToken ct)
    {
        if (ctx.PriorPointer is not null && ctx.Parameters.Perpetual)
        {
            var (envId, title) = await BridgeSessionApi.GetAsync(
                ctx.PriorPointer.SessionId,
                ctx.Parameters.BaseUrl,
                ctx.AccessToken!,
                orgUUID: "",
                ctx.HttpClient,
                ct).ConfigureAwait(false);
            if (envId is not null)
            {
                ctx.PriorSessionEnvId = envId;
                ctx.Logger?.LogInformation("Bridge v1: Perpetual 模式验证已有会话存活: env={EnvId} title={Title}",
                    envId, title ?? "(null)");
            }
            else
            {
                ctx.Logger?.LogWarning("Bridge v1: Perpetual 模式验证已有会话失败（可能已过期或被删除）");
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
