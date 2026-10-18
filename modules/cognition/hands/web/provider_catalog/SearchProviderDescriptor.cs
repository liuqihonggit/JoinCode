namespace Services.Web.ProviderCatalog;

/// <summary>
/// Search 供应商描述符适配器 — 包装 ISearchProvider 为统一 IProviderDescriptor（TASK054 §7）。
/// <para>Category = Search，SupportsOpenAiFormat = false（Search 供应商不走 OpenAI 协议）。</para>
/// <para>线程安全：ISearchProvider 实现自身负责线程安全。</para>
/// </summary>
internal sealed class SearchProviderDescriptor : IProviderDescriptor {
    private readonly ISearchProvider _provider;

    /// <summary>
    /// 构造 Search 供应商描述符适配器。
    /// </summary>
    /// <param name="provider">Search 供应商实例</param>
    public SearchProviderDescriptor(ISearchProvider provider) {
        _provider = provider;
    }

    /// <inheritdoc />
    public string Name => _provider.Name;

    /// <inheritdoc />
    public ProviderCategory Category => ProviderCategory.Search;

    /// <inheritdoc />
    public bool SupportsOpenAiFormat => false;

    /// <inheritdoc />
    public bool IsAvailable => _provider.IsAvailable;

    /// <inheritdoc />
    public string? DisplayName => null;
}
