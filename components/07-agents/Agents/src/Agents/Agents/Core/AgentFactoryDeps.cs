using JoinCode.Abstractions.Attributes;

namespace Core.Agents;

[Register]
public sealed record AgentFactoryDeps(
    IContextHierarchy? ContextHierarchy = null,
    IContextCompressor? ContextCompressor = null,
    ITelemetryService? TelemetryService = null,
    ILoggerFactory? LoggerFactory = null,
    IContextHierarchyFactory? ContextHierarchyFactory = null)
{
    public static AgentFactoryDeps FromServiceProvider(IServiceProvider sp)
    {
        return new AgentFactoryDeps(
            ContextHierarchy: sp.GetService<IContextHierarchy>(),
            ContextCompressor: sp.GetService<IContextCompressor>(),
            TelemetryService: sp.GetService<ITelemetryService>(),
            LoggerFactory: sp.GetService<ILoggerFactory>(),
            ContextHierarchyFactory: sp.GetService<IContextHierarchyFactory>());
    }
}
