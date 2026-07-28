namespace Core.Agents;

/// <summary>
/// 定义解析中间件 — 获取 Agent 定义
/// </summary>
[Register]
public sealed partial class DefinitionResolutionMiddleware : IAgentSpawnMiddleware
{
    [Inject] private readonly JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider _definitionProvider;

    /// <summary>定义解析最先执行</summary>

    /// <summary>定义解析失败应中断管道</summary>
    public JoinCode.Abstractions.Pipeline.ErrorBehavior OnError => JoinCode.Abstractions.Pipeline.ErrorBehavior.Propagate;

    public async Task InvokeAsync(AgentSpawnContext context, JoinCode.Abstractions.Pipeline.MiddlewareDelegate<AgentSpawnContext> next, CancellationToken ct)
    {
        context.Definition = !string.IsNullOrWhiteSpace(context.Options.AgentType)
            ? await _definitionProvider.GetAgentDefinitionAsync(context.Options.AgentType, cancellationToken: ct).ConfigureAwait(false)
            : null;

        await next(context, ct).ConfigureAwait(false);
    }
}
