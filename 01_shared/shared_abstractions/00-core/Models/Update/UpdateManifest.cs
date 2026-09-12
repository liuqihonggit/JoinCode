namespace JoinCode.Abstractions.Models.Update;

/// <summary>
/// 更新清单 — 从更新源获取的版本清单，描述所有可用版本
/// > ADR: 0064
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>
    /// 最新稳定版本号（如 "1.2.0"）
    /// </summary>
    [JsonPropertyName("latestVersion")]
    public required string LatestVersion { get; init; }

    /// <summary>
    /// 更新通道（stable/beta/canary）
    /// </summary>
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "stable";

    /// <summary>
    /// 可用版本列表（按发布时间降序）
    /// </summary>
    [JsonPropertyName("releases")]
    public required IReadOnlyList<UpdateManifestEntry> Releases { get; init; }
}

/// <summary>
/// 更新清单条目 — 描述单个版本的下载信息
/// </summary>
public sealed class UpdateManifestEntry
{
    /// <summary>
    /// 版本号（如 "1.2.0"）
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// 下载 URL（绝对 URL 或相对于清单地址的相对路径）
    /// </summary>
    [JsonPropertyName("downloadUrl")]
    public required string DownloadUrl { get; init; }

    /// <summary>
    /// SHA256 校验和（小写十六进制字符串），用于完整性校验
    /// </summary>
    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    /// <summary>
    /// 发布说明
    /// </summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; init; }

    /// <summary>
    /// 发布时间
    /// </summary>
    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>
    /// 最低可升级版本（如 "1.0.0"），低于此版本不允许升级到当前版本
    /// null 表示无限制
    /// </summary>
    [JsonPropertyName("minUpgradeFrom")]
    public string? MinUpgradeFrom { get; init; }
}

/// <summary>
/// 更新下载进度报告 — 命名加 Update 前缀避免与 Infrastructure.Network.Downloader.DownloadProgress 冲突
/// </summary>
public sealed class UpdateDownloadProgress
{
    /// <summary>
    /// 已下载字节数
    /// </summary>
    public long BytesDownloaded { get; init; }

    /// <summary>
    /// 总字节数（-1 表示未知）
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// 下载速度（字节/秒）
    /// </summary>
    public double BytesPerSecond { get; init; }

    /// <summary>
    /// 完成百分比（0-100，TotalBytes=-1 时为 -1）
    /// </summary>
    public double Percent => TotalBytes > 0 ? BytesDownloaded * 100.0 / TotalBytes : -1;
}

/// <summary>
/// 更新操作结果
/// </summary>
public sealed class UpdateResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 错误信息（失败时）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 下载的文件路径（下载成功时）
    /// </summary>
    public string? DownloadedPath { get; init; }

    /// <summary>
    /// 旧版本备份路径（替换成功时，用于回滚）
    /// </summary>
    public string? BackupPath { get; init; }

    /// <summary>
    /// 是否需要重启生效
    /// </summary>
    public bool RequiresRestart { get; init; }

    /// <summary>
    /// 成功工厂
    /// </summary>
    public static UpdateResult Succeeded(string? downloadedPath = null, string? backupPath = null, bool requiresRestart = true) => new()
    {
        Success = true,
        DownloadedPath = downloadedPath,
        BackupPath = backupPath,
        RequiresRestart = requiresRestart
    };

    /// <summary>
    /// 失败工厂
    /// </summary>
    public static UpdateResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
