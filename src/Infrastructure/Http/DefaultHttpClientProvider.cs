namespace Infrastructure.Http;

/// <summary>
/// 默认 HTTP 客户端提供者 — 优先通过 IHttpClientFactory 创建 HttpClient，无 factory 时 fallback 到共享 HttpClient
/// <para>P1-5 推广 HttpClientFactory: 让所有依赖 IHttpClientProvider 的服务间接受益于 Handler 池化</para>
/// <para>DI 容器构建后注入 IHttpClientFactory（通过 services.AddHttpClient() 启用）</para>
/// <para>DI 容器构建前的场景（HttpClientProviderFactory.Create()）使用无参构造函数 fallback</para>
/// </summary>
[Register(typeof(IHttpClientProvider))]
public sealed partial class DefaultHttpClientProvider : IHttpClientProvider
{
    private readonly IHttpClientFactory? _factory;
    private readonly HttpClient? _sharedClient;

    /// <summary>
    /// 无参构造函数 — fallback 到共享 HttpClient 实例
    /// 用于 HttpClientProviderFactory.Create() 路径（DI 容器构建前场景）
    /// </summary>
    public DefaultHttpClientProvider()
    {
        _factory = null;
        _sharedClient = new HttpClient();
    }

    /// <summary>
    /// 通过 IHttpClientFactory 创建 HttpClient — Handler 由 IHttpClientFactory 池化管理
    /// 用于 DI 容器构建后场景（主程序通过 services.AddHttpClient() 启用）
    /// </summary>
    /// <param name="factory">IHttpClientFactory 实例（必填，由 DI 注入）</param>
    public DefaultHttpClientProvider(IHttpClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _sharedClient = null;
    }

    /// <summary>
    /// 获取 HttpClient — 优先通过 IHttpClientFactory.CreateClient() 创建（Handler 池化），无 factory 时返回共享实例
    /// </summary>
    public HttpClient GetClient()
    {
        if (_factory is not null)
        {
            // P1-5: 通过 IHttpClientFactory.CreateClient() 获取 HttpClient
            // HttpClient 实例是轻量对象（每次新建），底层 HttpMessageHandler 由 IHttpClientFactory 池化管理
            return _factory.CreateClient(string.Empty);
        }
        return _sharedClient!;
    }

    /// <summary>
    /// 获取命名 HttpClient — 通过 IHttpClientFactory.CreateClient(name) 创建（支持不同配置的命名客户端）
    /// </summary>
    public HttpClient GetClient(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_factory is not null)
        {
            return _factory.CreateClient(name);
        }
        // fallback: 共享实例忽略 name
        return _sharedClient!;
    }
}
