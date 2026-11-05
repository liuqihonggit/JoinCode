namespace Services.Web.ProviderCatalog;

/// <summary>
/// Fetch 供应商描述符适配器 — 包装 IWebFetchProvider 为统一 IProviderDescriptor。
/// <para>Category = Fetch，SupportsOpenAiFormat = false（Fetch 供应商不走 OpenAI 协议）。</para>
/// <para>线程安全：IWebFetchProvider 实现自身负责线程安全。</para>
/// </summary>
internal sealed class FetchProviderDescriptor : IProviderDescriptor {
    private readonly IWebFetchProvider _provider;

    /// <summary>
    /// 构造 Fetch 供应商描述符适配器。
    /// </summary>
    /// <param name="provider">Fetch 供应商实例</param>
    public FetchProviderDescriptor(IWebFetchProvider provider) {
        _provider = provider;
    }

    /// <inheritdoc />
    public string Name => _provider.Name;

    /// <inheritdoc />
    public ProviderCategory Category => ProviderCategory.Fetch;

    /// <inheritdoc />
    public bool SupportsOpenAiFormat => false;

    /// <inheritdoc />
    public bool IsAvailable => _provider.IsAvailable;

    /// <inheritdoc />
    public string? DisplayName => null;
}
