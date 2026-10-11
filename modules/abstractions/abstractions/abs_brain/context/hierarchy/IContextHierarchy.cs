namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

public interface IContextHierarchy : IAsyncDisposable {
    /// <summary>获取或设置 Token 阈值。</summary>
    int TokenThreshold { get; set; }

    /// <summary>异步添加上下文层。</summary>
    Task AddLayerAsync(IContextLayer layer, CancellationToken ct = default);

    /// <summary>异步移除指定类型的上下文层。</summary>
    Task<bool> RemoveLayerAsync(ContextLayerType layerType, CancellationToken ct = default);

    /// <summary>异步获取指定类型的上下文层。</summary>
    Task<IContextLayer?> GetLayerAsync(ContextLayerType layerType, CancellationToken ct = default);

    /// <summary>异步获取所有上下文层列表。</summary>
    Task<IReadOnlyList<IContextLayer>> GetLayersAsync(CancellationToken ct = default);

    /// <summary>异步获取当前上下文层。</summary>
    Task<IContextLayer?> GetCurrentLayerAsync(CancellationToken ct = default);

    /// <summary>异步提升到指定上下文层。</summary>
    Task<IContextLayer> PromoteToLayerAsync(
        ContextLayerType targetLayer,
        Func<string, ContextLayerType, string> compressionFunc,
        CancellationToken ct = default);

    /// <summary>异步降级到指定上下文层。</summary>
    Task<bool> DemoteToLayerAsync(ContextLayerType sourceLayer, CancellationToken ct = default);

    /// <summary>异步获取有效上下文文本。</summary>
    Task<string> GetEffectiveContextAsync(CancellationToken ct = default);

    /// <summary>异步获取总 Token 数。</summary>
    Task<int> GetTotalTokenCountAsync(CancellationToken ct = default);
}