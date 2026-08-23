namespace Tools.Shell;

/// <summary>
/// Shell 命令拦截中间件 — 统一调度守卫链和拦截器链,替代旧 ShellCommandRewriteMiddleware
/// <para>
/// 集成位置:ShellValidationMiddleware 之后、ShellPathGateMiddleware 之前(与原 Rewrite 中间件同槽位)
/// 调用 <see cref="CommandInterceptionDispatcher"/> 统一处理:
/// <list type="bullet">
/// <item>守卫链:Allow 放行 | Rewrite 改写命令 | Deny/Redirect 短路 | Handoff 进拦截器链</item>
/// <item>拦截器链:Handled 短路 | Continue 放行</item>
/// </list>
/// </para>
/// <para>
/// Sed/Build 有状态拦截保留独立中间件(阶段C 不迁移),在管道后续槽位执行。
/// </para>
/// </summary>
[Register]
public sealed partial class ShellCommandInterceptionMiddleware : ServiceEntity, IShellMiddleware
{
    [Inject] private readonly CommandInterceptionDispatcher _dispatcher;
    [Inject] private readonly ILogger<ShellCommandInterceptionMiddleware>? _logger;

    /// <summary>
    /// 构造命令拦截中间件
    /// </summary>
    /// <param name="dispatcher">命令拦截调度器</param>
    /// <param name="logger">日志器(可选)</param>
    public ShellCommandInterceptionMiddleware(
        CommandInterceptionDispatcher dispatcher,
        ILogger<ShellCommandInterceptionMiddleware>? logger = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(
        ShellPipelineContext context,
        MiddlewareDelegate<ShellPipelineContext> next,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(context.Command))
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

#pragma warning disable JCC1001 // context 字典仅运行时传参,不参与 AOT 序列化
        var dispatchContext = new Dictionary<string, object>
        {
            ["ShellKind"] = context.Provider.Kind,
            ["WorkingDirectory"] = context.WorkingDirectory ?? string.Empty,
        };
#pragma warning restore JCC1001

        var outcome = await _dispatcher.DispatchAsync(context.Command, dispatchContext, ct).ConfigureAwait(false);

        if (outcome.ShortCircuitResult is not null)
        {
            _logger?.LogInformation(
                "命令被拦截短路: {Command}(原) → 短路结果",
                context.Command);
            context.Result = outcome.ShortCircuitResult;
            return;
        }

        if (outcome.FinalCommand != context.Command)
        {
            _logger?.LogInformation(
                "命令已改写: {Original} → {Rewritten}",
                context.Command, outcome.FinalCommand);
            context.Command = outcome.FinalCommand;
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
