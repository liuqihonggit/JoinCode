
namespace Core.Agents;

[Register]
public sealed partial class AgentServiceFactory : IAgentServiceFactory
{
    private readonly IChatClient _kernel;
    private readonly IChatContextManager _contextManager;
    private readonly WorkflowConfig _config;
    [Inject] private readonly ILogger<AgentService>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public AgentServiceFactory(
        IChatClient kernel,
        IChatContextManager contextManager,
        WorkflowConfig config,
        ILogger<AgentService>? logger = null,
        ITelemetryService? telemetryService = null)
    {
        _kernel = kernel;
        _contextManager = contextManager;
        _config = config;
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task<IAgent> CreateAsync(CancellationToken cancellationToken = default)
    {
        await _contextManager.LoadContextAsync(cancellationToken).ConfigureAwait(false);

        _telemetryService?.RecordCount("agent.factory.count", new Dictionary<string, string> { ["operation"] = "create_agent", ["success"] = true.ToString() }, "count", "Agent factory operation count");

        return new AgentService(
            _kernel,
            _contextManager,
            _config,
            _logger);
    }
}
