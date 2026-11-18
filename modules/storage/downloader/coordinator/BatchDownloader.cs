namespace Infrastructure.Network.Downloader.Coordinator;

/// <summary>
/// BatchDownloader — IBatchDownloader 实现,批量并行下载,DI Singleton
/// <para>单一职责:多 URL 并行协调,每个 item 内部复用 IDownloader 多线程分片</para>
/// <para>限流:工作池按并发数领取下标,无信号量</para>
/// <para>链式:items → DownloadAllAsync → results(顺序与输入一致)</para>
/// </summary>
[Register(typeof(IBatchDownloader), ServiceLifetime.Singleton)]
public sealed partial class BatchDownloader : ServiceEntity, IBatchDownloader {
    private readonly IDownloader _downloader;
    private readonly ILogger<BatchDownloader>? _logger;

    /// <summary>
    /// 构造 BatchDownloader(DI 注入)
    /// </summary>
    /// <param name="downloader">单文件下载器(复用其多线程分片能力)</param>
    /// <param name="logger">日志(可选)</param>
    public BatchDownloader(IDownloader downloader, ILogger<BatchDownloader>? logger = null) {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BatchDownloadResult[]> DownloadAllAsync(
        IReadOnlyList<BatchDownloadItem> items,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default) {
        if (items.Count == 0) return [];

        var effectiveConcurrency = Math.Max(1, maxConcurrency);
        var results = new BatchDownloadResult[items.Count];
        var nextIndex = -1;

        async Task WorkerAsync() {
            while (true) {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= items.Count) return;
                results[index] = await DownloadOneAsync(items[index], cancellationToken).ConfigureAwait(false);
            }
        }

        var workerCount = Math.Min(effectiveConcurrency, items.Count);
        await Task.WhenAll(Enumerable.Range(0, workerCount).Select(_ => WorkerAsync())).ConfigureAwait(false);

        return results;
    }

    private async Task<BatchDownloadResult> DownloadOneAsync(
        BatchDownloadItem item, CancellationToken ct) {
        var options = item.Options ?? new DownloadOptions { MaxThreads = 4, Resume = true };
        try {
            await using var session = _downloader.StartDownload(item.Url, item.FilePath, options, null, ct);
            var result = await session.WaitForCompletionAsync(ct).ConfigureAwait(false);
            return new BatchDownloadResult(item.FilePath, result.Success, result.ErrorMessage, result.DownloadedBytes);
        } catch (OperationCanceledException) {
            return new BatchDownloadResult(item.FilePath, false, "已取消", 0);
        } catch (Exception ex) {
            _logger?.LogError(ex, "批量下载失败: {Url} → {Path}", item.Url, item.FilePath);
            return new BatchDownloadResult(item.FilePath, false, ex.Message, 0);
        }
    }
}
