namespace Infrastructure.Network.Downloader;

/// <summary>
/// 下载结果 — 会话进入终态后由 WaitForCompletionAsync 返回
/// </summary>
/// <param name="Success">是否成功(Completed=true,Cancelled/Failed=false)</param>
/// <param name="FilePath">目标文件路径</param>
/// <param name="TotalBytes">文件总字节数</param>
/// <param name="DownloadedBytes">已下载字节数(成功时=TotalBytes)</param>
/// <param name="Elapsed">总耗时(含暂停时间)</param>
/// <param name="FinalState">最终状态(Completed/Cancelled/Failed)</param>
/// <param name="ErrorMessage">失败时的错误信息(Cancelled/Failed 时填充)</param>
public sealed record DownloadResult(
    bool Success,
    string FilePath,
    long TotalBytes,
    long DownloadedBytes,
    TimeSpan Elapsed,
    DownloadState FinalState,
    string? ErrorMessage = null);
