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
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellCommandInterceptionMiddleware : ServiceEntity, IShellMiddleware
{
    private readonly CommandInterceptionDispatcher _dispatcher;
    private readonly BashDefenseService _bashDefenseService;
    private readonly ILogger<ShellCommandInterceptionMiddleware>? _logger;

    /// <summary>
    /// 构造命令拦截中间件
    /// </summary>
    /// <param name="dispatcher">命令拦截调度器</param>
    /// <param name="bashDefenseService">Bash 防御服务（MTP 扰动纵深防御链）</param>
    /// <param name="logger">日志器(可选)</param>
    public ShellCommandInterceptionMiddleware(
        CommandInterceptionDispatcher dispatcher,
        BashDefenseService bashDefenseService,
        ILogger<ShellCommandInterceptionMiddleware>? logger = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _bashDefenseService = bashDefenseService ?? throw new ArgumentNullException(nameof(bashDefenseService));
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

        var dispatchContext = new GuardContext(
            context.Provider.Kind,
            context.WorkingDirectory ?? string.Empty);

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

        var defenseRejection = await EvaluateBashDefenseAsync(context, ct).ConfigureAwait(false);
        if (defenseRejection is not null)
        {
            _logger?.LogInformation("命令被 BashDefense 链拒绝: {Command}", context.Command);
            context.Result = defenseRejection;
            return;
        }

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行 BashDefense 链 — MTP 扰动纵深防御（重定向白名单 + 保留设备名检测）。
    /// <para>
    /// 在 CommandInterceptionDispatcher 之后执行，补充 Dispatcher 未覆盖的 MTP 扰动防御。
    /// 当前组装：CheckRetainedDevice + CheckRedirectWhitelist。
    /// RequireArgvHash 需要确认模式支持，后续从配置读取 ConfirmMode 后接入。
    /// </para>
    /// </summary>
    private async ValueTask<ToolResult?> EvaluateBashDefenseAsync(ShellPipelineContext context, CancellationToken ct)
    {
        var workDir = context.WorkingDirectory ?? string.Empty;
        var (_, rejection) = await _bashDefenseService
            .Begin(context.Command, workDir, context.Provider.Kind)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .Then(_bashDefenseService.CheckRedirectWhitelist)
            .ExecuteAsync(ct)
            .ConfigureAwait(false);
        return rejection;
    }
}
