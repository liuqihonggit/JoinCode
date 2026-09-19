namespace IO.Services.Update;

/// <summary>
/// Git 托管平台 Release 镜像更新源基类 — GitHub/GitLab/Gitea 共用 HTTP 下载逻辑
/// 子类只需重写 <see cref="GetLatestReleaseUrl"/> 和 <see cref="ParseRelease"/> 即可
/// > ADR: 0064
/// </summary>
public abstract class GitHostMirrorUpdateSourceBase : IUpdateSource {
    /// <summary>
    /// HTTP 客户端
    /// </summary>
    protected readonly HttpClient HttpClient;

    /// <summary>
    /// 镜像基础 URL（已去除尾部斜杠）
    /// </summary>
    protected readonly string MirrorBaseUrl;

    /// <summary>
    /// 日志器
    /// </summary>
    protected readonly ILogger? Logger;

    /// <summary>
    /// 构造 Git 托管平台镜像更新源基类
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="mirrorBaseUrl">镜像基础 URL</param>
    /// <param name="logger">日志器（可选）</param>
    protected GitHostMirrorUpdateSourceBase(
        HttpClient httpClient,
        string mirrorBaseUrl,
        ILogger? logger = null) {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        MirrorBaseUrl = mirrorBaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(mirrorBaseUrl));
        Logger = logger;
    }

    /// <summary>
    /// 更新源类型 — 子类实现
    /// </summary>
    public abstract UpdateSourceType Type { get; }

    /// <summary>
    /// 获取最新 Release 的 API URL — 子类实现（GitHub/GitLab/Gitea 路径不同）
    /// </summary>
    protected abstract string GetLatestReleaseUrl();

    /// <summary>
    /// 解析 Release JSON → <see cref="UpdateManifest"/> — 子类实现（各平台 JSON 格式不同）
    /// </summary>
    protected abstract UpdateManifest ParseRelease(string json);

    /// <summary>
    /// 拉取最新 Release 并解析为更新清单
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>更新清单；拉取或解析失败时返回 null</returns>
    public virtual async Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default) {
        try {
            var url = GetLatestReleaseUrl();
            Logger?.LogDebug("{TypeName}: 拉取最新 release {Url}", GetType().Name, url);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", BrandConstants.ProductName);

            using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseRelease(json);
        } catch (Exception ex) {
            Logger?.LogError(ex, "{TypeName}: 拉取失败 {Url}", GetType().Name, GetLatestReleaseUrl());
            return null;
        }
    }

    /// <summary>
    /// 下载指定清单条目的文件流
    /// </summary>
    /// <param name="entry">清单条目</param>
    /// <param name="progress">下载进度回调（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>文件流</returns>
    public virtual async Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(entry);

        Logger?.LogDebug("{TypeName}: 下载 {Url}", GetType().Name, entry.DownloadUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, entry.DownloadUrl);
        request.Headers.Add("User-Agent", BrandConstants.ProductName);

        var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 tag_name 提取版本号 — 去除前缀 v/V
    /// </summary>
    protected static string ExtractVersion(string tagName) =>
        tagName.StartsWith('v') || tagName.StartsWith('V') ? tagName[1..] : tagName;
}