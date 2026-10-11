namespace IO.Services.Update;

/// <summary>
/// GitHub Release 镜像更新源 — 镜像 GitHub API 响应格式，解决国内访问慢/超时
/// 从镜像服务器拉取 /releases/latest（GitHub API 格式），转换为 UpdateManifest
/// > ADR: 0064
/// </summary>
public sealed class GitHubMirrorUpdateSource : GitHostMirrorUpdateSourceBase {
    /// <summary>
    /// 构造 GitHub 镜像更新源
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="mirrorBaseUrl">镜像基础 URL</param>
    /// <param name="logger">日志器（可选）</param>
    public GitHubMirrorUpdateSource(HttpClient httpClient, string mirrorBaseUrl, ILogger<GitHubMirrorUpdateSource>? logger = null)
        : base(httpClient, mirrorBaseUrl, logger) {
    }

    /// <summary>
    /// 更新源类型 — GitHubMirror
    /// </summary>
    public override UpdateSourceType Type => UpdateSourceType.GitHubMirror;

    /// <summary>
    /// 获取最新 Release 的 API URL — GitHub /releases/latest 端点
    /// </summary>
    /// <returns>最新 Release 的 API URL</returns>
    protected override string GetLatestReleaseUrl() => $"{MirrorBaseUrl}/releases/latest";

    /// <summary>
    /// 解析 GitHub Release JSON 为更新清单
    /// </summary>
    /// <param name="json">Release JSON 文本</param>
    /// <returns>更新清单</returns>
    protected override UpdateManifest ParseRelease(string json) {
        var release = JsonSerializer.Deserialize(json, UpdateSourceJsonContext.Default.GitHubReleaseDto)
            ?? throw new InvalidOperationException("GitHub release JSON 解析失败");

        var tagName = release.TagName
            ?? throw new InvalidOperationException("GitHub release 缺少 tag_name");

        var version = ExtractVersion(tagName);
        var publishedAt = release.PublishedAt ?? DateTimeOffset.MinValue;
        var body = release.Body;

        var entries = new List<UpdateManifestEntry>();

        if (release.Assets is not null) {
            foreach (var asset in release.Assets) {
                var name = asset.Name;
                if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var downloadUrl = asset.BrowserDownloadUrl;
                if (downloadUrl is null) continue;

                var size = asset.Size ?? 0;

                entries.Add(new UpdateManifestEntry {
                    Version = version,
                    DownloadUrl = downloadUrl,
                    Sha256 = "",
                    SizeBytes = size,
                    ReleaseNotes = body,
                    PublishedAt = publishedAt,
                });
            }
        }

        return new UpdateManifest {
            LatestVersion = version,
            Channel = "stable",
            Releases = entries.AsReadOnly()
        };
    }
}