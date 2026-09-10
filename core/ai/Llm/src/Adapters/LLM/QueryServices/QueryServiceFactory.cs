namespace Api.LLM.QueryServices;


/// <summary>
/// QueryService 工厂 — 注册表模式,按 ProtocolKind 分派到对应派生类
/// <para>万物皆插件(ADR 0098): 供应商由 LlmProvidersPlugin 运行时注册</para>
/// <para>协议差异已下沉到派生类（OpenAIQueryService / AzureQueryService / AnthropicQueryService / AgnesQueryService / ResponsesQueryService）</para>
/// </summary>
public sealed class QueryServiceFactory : IQueryServiceFactory
{
    private readonly Dictionary<ProtocolKind, Func<ProviderConfig, HttpClient?, ILogger?, IFileSystem?, ResilientHttpExecutor?, IQueryService>> _providers = new();
    private Func<ProviderConfig, HttpClient?, ILogger?, IFileSystem?, ResilientHttpExecutor?, IQueryService>? _defaultFactory;

    /// <summary>
    /// 注册供应商工厂 — 插件加载时调用(ADR 0098)
    /// </summary>
    public void RegisterProvider(ProtocolKind kind, Func<ProviderConfig, HttpClient?, ILogger?, IFileSystem?, ResilientHttpExecutor?, IQueryService> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _providers[kind] = factory;
    }

    /// <summary>
    /// 注册默认供应商工厂(OpenAiCompatible 兜底) — 插件加载时调用
    /// </summary>
    public void RegisterDefault(Func<ProviderConfig, HttpClient?, ILogger?, IFileSystem?, ResilientHttpExecutor?, IQueryService> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _defaultFactory = factory;
    }

    /// <summary>
    /// 撤销供应商注册 — 插件卸载时调用
    /// </summary>
    public bool UnregisterProvider(ProtocolKind kind) => _providers.Remove(kind);

    IQueryService IQueryServiceFactory.Create(ProviderConfig config, HttpClient? httpClient, ILogger? logger, IFileSystem? fileSystem)
        => Create(config, httpClient, logger, fileSystem, resilientExecutor: null);

    public IQueryService Create(
        ProviderConfig config,
        HttpClient? httpClient = null,
        ILogger? logger = null,
        IFileSystem? fileSystem = null,
        ResilientHttpExecutor? resilientExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 兜底注入：当 ConfigLoader 未注入 Definition 时（如 Dream 组件），使用最小化定义
        if (config.Definition is null)
        {
            config.Definition = new FallbackProviderDefinition(config.ProtocolKind);
        }

        // 注册表查找 — 找不到则用默认工厂
        if (_providers.TryGetValue(config.ProtocolKind, out var factory))
        {
            return factory(config, httpClient, logger, fileSystem, resilientExecutor);
        }

        if (_defaultFactory is not null)
        {
            return _defaultFactory(config, httpClient, logger, fileSystem, resilientExecutor);
        }

        throw new InvalidOperationException($"[LLM-PROVIDER-NOT-FOUND] ProtocolKind '{config.ProtocolKind}' 未注册,且无默认供应商。请加载 LlmProvidersPlugin。");
    }
}
