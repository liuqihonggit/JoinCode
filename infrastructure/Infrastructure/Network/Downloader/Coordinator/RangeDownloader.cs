namespace Infrastructure.Network.Downloader.Coordinator;

/// <summary>
/// RangeDownloader — IDownloader 实现,下载入口
/// <para>创建 DownloadSession 并启动,返回可控制的会话</para>
/// <para>不挂 [Register](基建期不接入 DI),由调用方手动构造</para>
/// </summary>
public sealed class RangeDownloader : IDownloader
{
    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fs;
    private readonly TimeProvider? _clock;

    /// <summary>
    /// 构造 RangeDownloader
    /// </summary>
    /// <param name="httpClient">HttpClient(代理/超时由调用方配置)</param>
    /// <param name="fs">文件系统抽象(测试用 InMemoryFileSystem)</param>
    /// <param name="clock">时钟(测试注入,生产用 TimeProvider.System)</param>
    public RangeDownloader(HttpClient httpClient, IFileSystem fs, TimeProvider? clock = null)
    {
        _httpClient = httpClient;
        _fs = fs;
        _clock = clock;
    }

    /// <inheritdoc />
    public IDownloadSession StartDownload(
        string url,
        string filePath,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var session = new DownloadSession(_httpClient, _fs, url, filePath, options, progress, _clock);
        _ = session.StartAsync(cancellationToken);
        return session;
    }
}
