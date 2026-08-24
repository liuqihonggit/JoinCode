namespace Infrastructure.Network.Downloader;

/// <summary>
/// 断点续传元数据 — 持久化到 {目标文件路径}.meta.json,记录每个分片下载进度
/// <para>Resume 时读取此文件,校验 Url/ETag/LastModified 未变更后跳过已完成分片</para>
/// </summary>
public sealed class DownloadMetadata
{
    /// <summary>下载 URL(用于 Resume 时校验资源未变更)</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>文件总字节数</summary>
    public long TotalLength { get; set; }

    /// <summary>ETag(服务器返回,用于校验资源未变更;可能为 null)</summary>
    public string? ETag { get; set; }

    /// <summary>Last-Modified(服务器返回,用于校验资源未变更;可能为 null)</summary>
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>分片进度列表(按 Index 排序)</summary>
    public List<Planning.DownloadChunk> Chunks { get; set; } = [];

    /// <summary>创建时间(用于诊断过期元数据)</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最后更新时间(用于诊断)</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
