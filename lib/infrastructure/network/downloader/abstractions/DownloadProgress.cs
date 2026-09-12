namespace Infrastructure.Network.Downloader;

/// <summary>
/// 下载进度报告 — 通过 IProgress&lt;DownloadProgress&gt; 回调推送
/// </summary>
/// <param name="TotalBytes">文件总字节数(探测前为 0)</param>
/// <param name="DownloadedBytes">已下载字节数</param>
/// <param name="SpeedBps">当前下载速度(字节/秒)</param>
/// <param name="Percent">完成百分比(0~100)</param>
/// <param name="State">当前会话状态</param>
/// <param name="IsResumed">本次下载是否为断点续传(从 .meta.json 恢复)</param>
public sealed record DownloadProgress(
    long TotalBytes,
    long DownloadedBytes,
    double SpeedBps,
    double Percent,
    DownloadState State,
    bool IsResumed);
