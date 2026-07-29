namespace Core.Context;

[Register(typeof(IContextHierarchyFactory))]
public sealed partial class ContextHierarchyFactory : IContextHierarchyFactory
{
    [Inject] private readonly ILogger<ContextHierarchyFactory>? _logger;

    JoinCode.Abstractions.Brain.Context.Hierarchy.IContextHierarchy IContextHierarchyFactory.Create(JoinCode.Abstractions.Brain.Context.Hierarchy.ContextHierarchyOptions options)
    {
        var brainOptions = new ContextHierarchyOptions
        {
            TokenThreshold = options.TokenThreshold,
            AutoCompressionEnabled = options.AutoCompressionEnabled,
            MaxLayers = options.MaxLayers,
            DefaultCompressionRatio = options.DefaultCompressionRatio
        };
        return ContextHierarchy.Create(brainOptions, _logger as ILogger<ContextHierarchy>);
    }
}
