namespace Infrastructure.Network.Downloader.Chunk;

/// <summary>
/// 单分片下载结果 — 记录一个分片下载的 outcome
/// </summary>
/// <param name="Success">是否成功</param>
/// <param name="ChunkIndex">分片序号</param>
/// <param name="BytesDownloaded">本次下载字节数(不含续传的已下载部分)</param>
/// <param name="ErrorMessage">失败时的错误信息</param>
internal sealed record ChunkDownloadResult(
    bool Success,
    int ChunkIndex,
    long BytesDownloaded,
    string? ErrorMessage);
