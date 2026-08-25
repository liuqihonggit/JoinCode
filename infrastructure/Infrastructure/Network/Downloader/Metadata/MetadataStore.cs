namespace Infrastructure.Network.Downloader.Metadata;

/// <summary>
/// 元数据存储 — 读写 {filePath}.meta.json,支持断点续传
/// <para>AOT 兼容:用 DownloaderJsonContext 源码生成器序列化</para>
/// <para>文件 IO:通过 IFileSystem 抽象层注入(JCC9001),测试用 InMemoryFileSystem 零磁盘 IO</para>
/// <para>并发策略:MetadataStore 不支持并发写,调用方用 ConcurrentDictionary 累积后单次写入</para>
/// </summary>
internal sealed class MetadataStore
{
    private readonly IFileSystem _fs;

    internal MetadataStore(IFileSystem fs)
    {
        _fs = fs;
    }

    /// <summary>元数据文件路径 = filePath + .meta.json</summary>
    internal static string GetMetadataPath(string filePath) => filePath + ".meta.json";

    /// <summary>
    /// 尝试加载元数据(文件不存在返回 null,损坏 JSON 返回 null)
    /// </summary>
    internal DownloadMetadata? TryLoad(string filePath)
    {
        var metaPath = GetMetadataPath(filePath);
        if (!_fs.FileExists(metaPath)) return null;

        try
        {
            var json = _fs.ReadAllText(metaPath);
            return JsonSerializer.Deserialize(json, DownloaderJsonContext.Default.DownloadMetadata);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 保存元数据(覆盖写入,自动更新 UpdatedAt 和 CreatedAt)
    /// </summary>
    internal void Save(string filePath, DownloadMetadata metadata)
    {
        var metaPath = GetMetadataPath(filePath);
        metadata.UpdatedAt = DateTimeOffset.UtcNow;
        if (metadata.CreatedAt == default)
            metadata.CreatedAt = metadata.UpdatedAt;

        var json = JsonSerializer.Serialize(metadata, DownloaderJsonContext.Default.DownloadMetadata);
        _fs.WriteAllText(metaPath, json);
    }

    /// <summary>
    /// 删除元数据文件(不存在则无操作)
    /// </summary>
    internal void Delete(string filePath)
    {
        var metaPath = GetMetadataPath(filePath);
        if (_fs.FileExists(metaPath)) _fs.DeleteFile(metaPath);
    }

    /// <summary>
    /// 校验元数据是否匹配当前资源(URL + ETag + LastModified)
    /// <para>LastModified 容差 1s(HTTP 日期精度秒级)</para>
    /// </summary>
    internal static bool Matches(DownloadMetadata metadata, string url, string? eTag, DateTimeOffset? lastModified)
    {
        if (!string.Equals(metadata.Url, url, StringComparison.Ordinal))
            return false;

        if (!string.Equals(metadata.ETag, eTag, StringComparison.Ordinal))
            return false;

        if (!LastModifiedMatches(metadata.LastModified, lastModified))
            return false;

        return true;
    }

    private static bool LastModifiedMatches(DateTimeOffset? stored, DateTimeOffset? current)
    {
        if (stored.HasValue != current.HasValue) return false;
        if (!stored.HasValue) return true;
        return Math.Abs((stored.Value - current!.Value).TotalSeconds) <= 1;
    }
}
