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
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("GitHub release 缺少 tag_name");

        var version = ExtractVersion(tagName);
        var publishedAt = root.TryGetProperty("published_at", out var pubEl) ? pubEl.GetDateTimeOffset() : DateTimeOffset.MinValue;
        var body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

        var entries = new List<UpdateManifestEntry>();

        if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array) {
            foreach (var asset in assetsEl.EnumerateArray()) {
                var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                if (downloadUrl is null) continue;

                var size = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;

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