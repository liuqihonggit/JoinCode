namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 升级服务 — 版本检查 + 下载更新 + 应用更新（原子替换）
/// > ADR: 0064
/// </summary>
public interface IUpgradeService
{
    /// <summary>
    /// 获取当前版本
    /// </summary>
    Version GetCurrentVersion();

    /// <summary>
    /// 获取最新版本（从更新源或 GitHub API）
    /// </summary>
    Task<Version?> GetLatestVersionAsync(CancellationToken ct = default);

    /// <summary>
    /// 是否有可用更新
    /// </summary>
    Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取最新版本的清单条目 — 包含下载 URL、SHA256、发布说明等
    /// </summary>
    /// <returns>清单条目；无更新或获取失败返回 null</returns>
    Task<UpdateManifestEntry?> GetUpdateEntryAsync(CancellationToken ct = default);

    /// <summary>
    /// 下载更新到临时目录 — 下载完成后进行 SHA256 校验
    /// </summary>
    /// <param name="entry">清单条目</param>
    /// <param name="progress">下载进度回调（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>下载结果（成功时 DownloadedPath 为下载的文件路径）</returns>
    Task<UpdateResult> DownloadUpdateAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// 应用更新 — 原子替换当前 exe（备份→替换→失败回滚）
    /// Windows 上运行中 exe 可重命名，替换后需重启生效
    /// </summary>
    /// <param name="downloadedExePath">已下载的新 exe 路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>应用结果（成功时 BackupPath 为旧版本备份路径，RequiresRestart=true）</returns>
    Task<UpdateResult> ApplyUpdateAsync(string downloadedExePath, CancellationToken ct = default);
}
