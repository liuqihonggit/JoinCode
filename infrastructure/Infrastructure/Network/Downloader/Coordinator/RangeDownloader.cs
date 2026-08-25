namespace Infrastructure.Network.Downloader.Coordinator;

/// <summary>
/// RangeDownloader — IDownloader 实现,下载入口,DI Singleton
/// <para>创建 DownloadSession 并启动,返回可控制的会话</para>
/// <para>通过 IHttpClientProvider 获取 HttpClient(Handler 池化,代理/超时由调用方配置)</para>
/// </summary>
[Register(typeof(IDownloader), ServiceLifetime.Singleton)]
public sealed partial class RangeDownloader : ServiceEntity, IDownloader
{
    private readonly IHttpClientProvider _httpClientProvider;
    private readonly IFileSystem _fs;
    private readonly TimeProvider? _clock;
    private readonly ILogger<RangeDownloader>? _logger;

    /// <summary>
    /// 构造 RangeDownloader(DI 注入)
    /// </summary>
    /// <param name="httpClientProvider">HTTP 客户端提供者(Handler 池化)</param>
    /// <param name="fs">文件系统抽象(测试用 InMemoryFileSystem)</param>
    /// <param name="clock">时钟(测试注入,生产用 TimeProvider.System)</param>
    /// <param name="logger">日志(可选)</param>
    public RangeDownloader(
        IHttpClientProvider httpClientProvider,
        IFileSystem fs,
        TimeProvider? clock = null,
        ILogger<RangeDownloader>? logger = null)
    {
        _httpClientProvider = httpClientProvider;
        _fs = fs;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public IDownloadSession StartDownload(
        string url,
        string filePath,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientProvider.GetClient();
        var session = new DownloadSession(httpClient, _fs, url, filePath, options, progress, _clock);
        _ = session.StartAsync(cancellationToken);
        return session;
    }
}
