namespace Core.Scheduling.Tasks;


[Register(typeof(ITeammateExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammateSpawnMiddleware : ServiceEntity, ITeammateExecutionMiddleware
{

    public TeammateSpawnMiddleware(IAgentLifecycleManager agentLifecycleManager, ISubAgentContextAccessor subAgentContextAccessor)
    {
        _agentLifecycleManager = agentLifecycleManager;
        _subAgentContextAccessor = subAgentContextAccessor;
    }
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;


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
            SessionId = _subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId,
        };

        var agent = await _agentLifecycleManager.SpawnSubAgentAsync(definition.Task, options, ct).ConfigureAwait(false);

        if (definition.InitialContext is { Count: > 0 })
        {
            foreach (var initialCtx in definition.InitialContext)
            {
                ((AgentBase)agent).AddContext(initialCtx);
            }
        }

        ctx.Agent = agent;

        await next(ctx, ct).ConfigureAwait(false);
    }
}
