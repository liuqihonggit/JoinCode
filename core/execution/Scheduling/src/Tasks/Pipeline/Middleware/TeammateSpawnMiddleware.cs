namespace Core.Scheduling.Tasks;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(ITeammateExecutionMiddleware))]
public sealed partial class TeammateSpawnMiddleware : ITeammateExecutionMiddleware
{
    [Inject] private readonly IAgentLifecycleManager _agentLifecycleManager;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;


    public async Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        var definition = ctx.Definition;

        var options = new SubAgentOptions
        {
            Role = definition.Role != default ? definition.Role : AgentRole.Executor,
            Variant = definition.Variant,
            AdditionalInstructions = definition.AdditionalInstructions,
            MaxIterations = definition.MaxIterations,
            ContentReplacementState = _subAgentContextAccessor.Current?.ContentReplacementState?.Clone(),
            SessionId = _subAgentContextAccessor.Current?.SessionId ?? "default",
        };

        var agent = await _agentLifecycleManager.SpawnSubAgentAsync(definition.Task, options, ct).ConfigureAwait(false);

        if (definition.InitialContext is { Count: > 0 })
        {
            foreach (var initialCtx in definition.InitialContext)
            {
                ((Agent)agent).AddContext(initialCtx);
            }
        }

        ctx.Agent = agent;

        await next(ctx, ct).ConfigureAwait(false);
    }
}
