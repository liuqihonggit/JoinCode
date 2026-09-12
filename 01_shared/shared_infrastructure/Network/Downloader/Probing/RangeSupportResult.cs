namespace Infrastructure.Network.Downloader.Probing;

/// <summary>
/// Range 支持探测结果 — 描述服务器是否支持断点续传及文件元信息
/// </summary>
/// <param name="SupportsRange">是否支持 Range 请求(Accept-Ranges: bytes 或响应 206)</param>
/// <param name="ContentLength">文件总字节数(可能为 null)</param>
/// <param name="ETag">ETag(用于续传校验,带引号;可能为 null)</param>
/// <param name="LastModified">Last-Modified(用于续传校验;可能为 null)</param>
internal sealed record RangeSupportResult(
    bool SupportsRange,
    long? ContentLength,
    string? ETag,
    DateTimeOffset? LastModified);
