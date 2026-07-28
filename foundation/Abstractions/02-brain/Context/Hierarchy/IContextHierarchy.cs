namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

public interface IContextHierarchy : IDisposable
{
    int TokenThreshold { get; set; }

    Task AddLayerAsync(IContextLayer layer, CancellationToken ct = default);

    Task<bool> RemoveLayerAsync(ContextLayerType layerType, CancellationToken ct = default);

    Task<IContextLayer?> GetLayerAsync(ContextLayerType layerType, CancellationToken ct = default);

    Task<IReadOnlyList<IContextLayer>> GetLayersAsync(CancellationToken ct = default);

    Task<IContextLayer?> GetCurrentLayerAsync(CancellationToken ct = default);

    Task<IContextLayer> PromoteToLayerAsync(
        ContextLayerType targetLayer,
        Func<string, ContextLayerType, string> compressionFunc,
        CancellationToken ct = default);

    Task<bool> DemoteToLayerAsync(ContextLayerType sourceLayer, CancellationToken ct = default);

    Task<string> GetEffectiveContextAsync(CancellationToken ct = default);

    Task<int> GetTotalTokenCountAsync(CancellationToken ct = default);
}
