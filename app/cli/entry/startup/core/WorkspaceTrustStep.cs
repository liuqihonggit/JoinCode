namespace JoinCode.Entry;

/// <summary>
/// 工作目录信任检查中间件
/// </summary>
[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class WorkspaceTrustStep : ServiceEntity, IMiddleware<StartupContext>
{
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        if (!await StartupWorkflow.CheckWorkspaceTrustAsync(context.Options, context.FileSystem))
            return;  // 短路

        await next(context, ct);
    }
}
