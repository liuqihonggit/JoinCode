namespace Services.Web.ProviderCatalog;

/// <summary>
/// 供应商目录实现 — 从 IProviderDefinitionRegistry(LLM) + IEnumerable&lt;ISearchProvider&gt;(Search) 动态构建（TASK054 §7）。
/// <para>LLM 供应商从 registry 适配为 LlmProviderDescriptor，Search 供应商适配为 SearchProviderDescriptor。</para>
/// <para>线程安全：构建后只读，FrozenDictionary/FrozenList 自身线程安全。</para>
/// </summary>
[Register(typeof(IProviderCatalog), ServiceLifetime.Singleton)]
public sealed class ProviderCatalog : IProviderCatalog {
    private readonly FrozenDictionary<string, IProviderDescriptor> _byName;
    private readonly IReadOnlyList<IProviderDescriptor> _all;

    /// <summary>
    /// 构造供应商目录。
    /// </summary>
    /// <param name="llmRegistry">LLM 供应商注册表（运行时从 settings.json 构建）</param>
    /// <param name="searchProviders">Search 供应商集合（DI 收集所有 [Register(typeof(ISearchProvider))] 标记的类）</param>
    public ProviderCatalog(
        IProviderDefinitionRegistry llmRegistry,
        IEnumerable<ISearchProvider> searchProviders
    ) {
        var llmDescriptors = llmRegistry.GetRegisteredProviders()
            .SelectMany(name => llmRegistry.TryGet(name) is { } def
                ? new[] { new LlmProviderDescriptor(def) }
                : Array.Empty<LlmProviderDescriptor>());

        var searchDescriptors = searchProviders
            .Select(p => (IProviderDescriptor)new SearchProviderDescriptor(p));

        _all = llmDescriptors
            .Cast<IProviderDescriptor>()
            .Concat(searchDescriptors)
            .ToImmutableArray();
        _byName = FrozenDictionary.ToFrozenDictionary(_all, d => d.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<IProviderDescriptor> GetAll() => _all;

    /// <inheritdoc />
    public IReadOnlyList<IProviderDescriptor> GetByCategory(ProviderCategory category)
        => _all.Where(d => d.Category == category).ToImmutableArray();

    /// <inheritdoc />
    public bool TryGet(string name, [NotNullWhen(true)] out IProviderDescriptor? descriptor)
        => _byName.TryGetValue(name, out descriptor);
}
