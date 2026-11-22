namespace Core.Context;

/// <summary>
/// 工具列表注入中间件 — 注入 Agent/Skill 列表附件
/// </summary>
[Register(typeof(IPreparePreprocessMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ToolListingInjectionMiddleware : ServiceEntity, IPreparePreprocessMiddleware {

    /// <summary>
    /// 初始化工具列表注入中间件
    /// </summary>
    /// <param name="toolListingService">工具列表服务，null 时跳过注入</param>
    public ToolListingInjectionMiddleware(Prompts.Services.ToolListingService? toolListingService = null) {
        _toolListingService = toolListingService;
    }
    private readonly Prompts.Services.ToolListingService? _toolListingService;

    /// <summary>错误行为策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct) {
        if (_toolListingService is not null) {
            await _toolListingService.InjectAgentListingAsync(ct: ct).ConfigureAwait(false);
            await _toolListingService.InjectSkillListingAsync(ct: ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}