namespace JoinCode.Entry;

/// <summary>
/// 工作目录信任检查中间件
/// </summary>
[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class WorkspaceTrustStep : ServiceEntity, IMiddleware<StartupContext> {
    /// <summary>执行工作目录信任检查中间件 — 校验当前工作目录是否受信任，未受信任时短路终止管道</summary>
    /// <param name="context">启动上下文，包含 CLI 选项与文件系统</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct) {
        if (!await StartupWorkflow.CheckWorkspaceTrustAsync(context.Options, context.FileSystem).ConfigureAwait(false))
            return;  // 短路

        await next(context, ct).ConfigureAwait(false);
    }
}