using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 工具列表注入中间件 — 注入 Agent/Skill 列表附件
/// </summary>
[Register(typeof(IPreparePreprocessMiddleware))]
public sealed partial class ToolListingInjectionMiddleware : IPreparePreprocessMiddleware
{
    [Inject] private readonly Prompts.Services.ToolListingService? _toolListingService;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct)
    {
        if (_toolListingService is not null)
        {
            await _toolListingService.InjectAgentListingAsync(ct: ct).ConfigureAwait(false);
            await _toolListingService.InjectSkillListingAsync(ct: ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
