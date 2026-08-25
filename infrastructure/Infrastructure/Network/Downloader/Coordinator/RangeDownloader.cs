namespace Infrastructure.Network.Downloader.Coordinator;

/// <summary>
/// RangeDownloader — IDownloader 实现,下载入口,DI Singleton
/// <para>创建 DownloadSession 并启动,返回可控制的会话</para>
/// <para>通过 IHttpClientProvider 获取 HttpClient(Handler 池化,代理/超时由调用方配置)</para>
/// <para>代理支持:DownloadOptions.ProxyUrl > HTTPS_PROXY/HTTP_PROXY 环境变量 > VPN/代理路由识别</para>
/// </summary>
[Register(typeof(IDownloader), ServiceLifetime.Singleton)]
public sealed partial class RangeDownloader : ServiceEntity, IDownloader
{
    private readonly IHttpClientProvider _httpClientProvider;
    private readonly IFileSystem _fs;
    private readonly TimeProvider? _clock;
    private readonly ILogger<RangeDownloader>? _logger;
    private readonly INetworkConnectivityService? _networkService;
    private readonly ConcurrentDictionary<string, HttpClient> _proxiedClients = new();

    /// <summary>
    /// 构造 RangeDownloader(DI 注入)
    /// </summary>
    /// <param name="httpClientProvider">HTTP 客户端提供者(Handler 池化)</param>
    /// <param name="fs">文件系统抽象(测试用 InMemoryFileSystem)</param>
    /// <param name="clock">时钟(测试注入,生产用 TimeProvider.System)</param>
    /// <param name="logger">日志(可选)</param>
    /// <param name="networkService">网络连接性服务(可选,用于 VPN/代理路由识别)</param>
    public RangeDownloader(
        IHttpClientProvider httpClientProvider,
        IFileSystem fs,
        TimeProvider? clock = null,
        ILogger<RangeDownloader>? logger = null,
        INetworkConnectivityService? networkService = null)
    {
        _httpClientProvider = httpClientProvider;
        _fs = fs;
        _clock = clock;
        _logger = logger;
        _networkService = networkService;
    }

    /// <inheritdoc />
    public IDownloadSession StartDownload(
        string url,
        string filePath,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var proxyUrl = ResolveProxyUrl(options?.ProxyUrl, _networkService);
        var httpClient = GetHttpClient(proxyUrl);
        var session = new DownloadSession(httpClient, _fs, url, filePath, options, progress, _clock);
        _ = session.StartAsync(cancellationToken);
        return session;
    }

    /// <summary>
    /// 解析代理 URL — 优先级:DownloadOptions.ProxyUrl > HTTPS_PROXY/HTTP_PROXY 环境变量 > VPN/代理路由
    /// </summary>
    /// <param name="optionsProxy">DownloadOptions.ProxyUrl(显式指定,最高优先级)</param>
    /// <param name="networkService">网络连接性服务(可选,用于 VPN/代理路由识别)</param>
    /// <returns>代理 URL(无代理时返回 null)</returns>
    internal static string? ResolveProxyUrl(string? optionsProxy, INetworkConnectivityService? networkService)
    {
        if (!string.IsNullOrWhiteSpace(optionsProxy))
            return optionsProxy;

        var envProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY")
                    ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
                    ?? Environment.GetEnvironmentVariable("https_proxy")
                    ?? Environment.GetEnvironmentVariable("http_proxy");
        if (!string.IsNullOrWhiteSpace(envProxy))
            return envProxy;

        if (networkService is not null)
        {
            var route = networkService.GetCurrentRoute();
            if (!string.IsNullOrWhiteSpace(route.ProxyUrl))
                return route.ProxyUrl;
        }

        return null;
    }

    /// <summary>
    /// 获取 HttpClient — 有代理时创建带代理的 HttpClient(按 proxyUrl 缓存),无代理时用 IHttpClientProvider
    /// </summary>
    internal HttpClient GetHttpClient(string? proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl))
            return _httpClientProvider.GetClient();

        return _proxiedClients.GetOrAdd(proxyUrl!, url =>
        {
            var handler = new HttpClientHandler { Proxy = new WebProxy(url) };
            return new HttpClient(handler);
        });
    }
}
