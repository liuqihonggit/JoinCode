namespace Infrastructure.Network.Downloader;

/// <summary>
/// 批量并行下载器 — 多 URL 并行下载到各自文件,单一职责:批量并行协调
/// <para>链式:输入 BatchDownloadItem 列表 → 并行下载 → 返回 BatchDownloadResult 列表</para>
/// <para>限流:SemaphoreSlim 控制最大并发数,0 线程阻塞</para>
/// <para>每个 item 内部复用 IDownloader 多线程分片,实现双层并行(item 间 + 分片间)</para>
/// </summary>
public interface IBatchDownloader {
    /// <summary>
    /// 批量并行下载 — 所有 item 并行下载,返回各自结果
    /// </summary>
    /// <param name="items">下载项列表(url → filePath 映射)</param>
    /// <param name="maxConcurrency">item 间最大并发数(默认 4)</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>每个 item 的下载结果(顺序与输入一致)</returns>
    Task<BatchDownloadResult[]> DownloadAllAsync(
        IReadOnlyList<BatchDownloadItem> items,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 批量下载项 — 一个 URL 下载到一个文件
/// </summary>
/// <param name="Url">下载 URL</param>
/// <param name="FilePath">目标文件路径</param>
/// <param name="Options">下载选项(null=默认多线程分片+断点续传)</param>
public sealed record BatchDownloadItem(
    string Url,
    string FilePath,
    DownloadOptions? Options = null);

/// <summary>
/// 批量下载结果 — 单个 item 的下载结果
/// </summary>
/// <param name="FilePath">目标文件路径</param>
/// <param name="Success">是否成功</param>
/// <param name="Error">错误信息(失败时)</param>
/// <param name="BytesDownloaded">已下载字节数</param>
public sealed record BatchDownloadResult(
    string FilePath,
    bool Success,
    string? Error,
    long BytesDownloaded);
