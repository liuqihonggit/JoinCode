namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 更新源抽象 — 从不同类型的更新源获取版本清单和下载二进制
/// 四种实现：StaticFile / HttpApi / GitHubMirror / LocalFile（见 <see cref="UpdateSourceType"/>）
/// > ADR: 0064
/// </summary>
public interface IUpdateSource {
    /// <summary>
    /// 更新源类型
    /// </summary>
    UpdateSourceType Type { get; }

    /// <summary>
    /// 获取更新清单 — 包含所有可用版本信息
    /// </summary>
    /// <returns>清单；获取失败返回 null</returns>
    Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default);

    /// <summary>
    /// 下载指定版本的二进制流
    /// </summary>
    /// <param name="entry">清单条目（包含下载 URL 和校验信息）</param>
    /// <param name="progress">下载进度回调（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>二进制流；下载失败抛异常</returns>
    Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// 获取下载 URL — 单一职责:只告诉"从哪下",实际下载交给 IDownloader 多线程分片
    /// <para>远程源(StaticFile/GitHostMirror/HttpApi)返回绝对 URL;本地源(LocalFile)返回 null</para>
    /// <para>返回 null 时,调用方回退 DownloadAsync(Stream) 做本地文件复制</para>
    /// </summary>
    /// <param name="entry">清单条目</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>下载 URL(远程源)或 null(本地源)</returns>
    Task<string?> GetDownloadUrlAsync(
        UpdateManifestEntry entry,
        CancellationToken ct = default);
}