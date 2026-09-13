namespace Infrastructure.Network.Downloader;

/// <summary>
/// 可控制的下载会话 — 暴露暂停/继续/结束/等待完成操作
/// <para>状态流转由 DownloadStateMachine 校验,非法转换抛 InvalidOperationException[DOWN001]</para>
/// <para>线程安全:Pause/Resume/Cancel 可从 UI 线程调用,下载在工作线程</para>
/// </summary>
public interface IDownloadSession : IAsyncDisposable
{
    /// <summary>当前状态(线程安全读取,volatile 语义)</summary>
    DownloadState State { get; }

    /// <summary>
    /// 暂停下载(优雅停止:等待当前分片写完当前块,持久化元数据,释放 HTTP 连接)
    /// <para>仅 Downloading 状态可调用,转换到 Paused</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">非 Downloading 状态调用时抛 [DOWN001]</exception>
    Task PauseAsync(CancellationToken ct = default);

    /// <summary>
    /// 继续下载(从暂停处恢复:读取元数据,校验资源未变更,跳过已完成分片,继续未完成分片)
    /// <para>仅 Paused 状态可调用,转换到 Downloading</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">非 Paused 状态调用时抛 [DOWN001]</exception>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>
    /// 取消下载(彻底取消:中断所有分片,清理 .part 和 .meta.json)
    /// <para>任意非终态可调用,转换到 Cancelled</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">终态调用时抛 [DOWN001]</exception>
    Task CancelAsync(CancellationToken ct = default);

    /// <summary>
    /// 等待完成(阻塞直到 Completed/Cancelled/Failed 终态)
    /// </summary>
    /// <returns>最终结果:Cancelled→Success=false,Failed→Success=false+ErrorMessage</returns>
    Task<DownloadResult> WaitForCompletionAsync(CancellationToken ct = default);
}
