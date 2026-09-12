namespace Infrastructure.Network.Downloader;

/// <summary>
/// 下载器入口接口 — 启动下载并返回可控制的会话
/// <para>非阻塞:StartDownload 立即返回 IDownloadSession,下载在后台进行</para>
/// <para>控制:通过 IDownloadSession.PauseAsync/ResumeAsync/CancelAsync 控制状态流转</para>
/// </summary>
public interface IDownloader
{
    /// <summary>
    /// 启动下载,返回可控制的会话(非阻塞,立即返回)
    /// </summary>
    /// <param name="url">下载 URL</param>
    /// <param name="filePath">目标文件路径(下载完成后写入此路径)</param>
    /// <param name="options">下载选项(null=默认单线程+断点续传)</param>
    /// <param name="progress">进度回调(null=不报告进度)</param>
    /// <param name="cancellationToken">取消令牌(取消=结束下载,非暂停)</param>
    /// <returns>可控制的下载会话,初始状态 Idle→Downloading</returns>
    IDownloadSession StartDownload(
        string url,
        string filePath,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
