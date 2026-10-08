namespace IO.Services.Update;

/// <summary>
/// Gitea Release 镜像更新源 — 从 Gitea API 拉取 Release，转换为 UpdateManifest
/// Gitea API 格式: GET /repos/:owner/:repo/releases → [{ tag_name, assets: [{ name, download_url, size }] }]
/// > ADR: 0064
/// </summary>
public sealed class GiteaMirrorUpdateSource : GitHostMirrorUpdateSourceBase {
    /// <summary>
    /// 构造 Gitea 镜像更新源
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="mirrorBaseUrl">镜像基础 URL</param>
    /// <param name="logger">日志器（可选）</param>
    public GiteaMirrorUpdateSource(HttpClient httpClient, string mirrorBaseUrl, ILogger<GiteaMirrorUpdateSource>? logger = null)
        : base(httpClient, mirrorBaseUrl, logger) {
    }

    /// <summary>
    /// 更新源类型 — GiteaMirror
    /// </summary>
    public override UpdateSourceType Type => UpdateSourceType.GiteaMirror;

    /// <summary>
    /// 获取最新 Release 的 API URL — Gitea /releases 端点
    /// </summary>
    /// <returns>最新 Release 的 API URL</returns>
    protected override string GetLatestReleaseUrl() => $"{MirrorBaseUrl}/releases";

    /// <summary>
    /// 解析 Gitea Release JSON 为更新清单
    /// </summary>
    /// <param name="json">Release JSON 文本</param>
    /// <returns>更新清单</returns>
    protected override UpdateManifest ParseRelease(string json) {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Gitea /releases 返回数组，取第一个（最新）
        GiteaReleaseDto latestRelease;
        if (root.ValueKind == JsonValueKind.Array) {
            var first = root.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("Gitea releases 为空");
            latestRelease = first.Deserialize(UpdateSourceJsonContext.Default.GiteaReleaseDto)
                ?? throw new InvalidOperationException("Gitea release JSON 解析失败");
        } else {
            latestRelease = root.Deserialize(UpdateSourceJsonContext.Default.GiteaReleaseDto)
                ?? throw new InvalidOperationException("Gitea release JSON 解析失败");
        }

        var tagName = latestRelease.TagName
            ?? throw new InvalidOperationException("Gitea release 缺少 tag_name");

        var version = ExtractVersion(tagName);
        var publishedAt = latestRelease.CreatedAt ?? DateTimeOffset.MinValue;
        var body = latestRelease.Body;

        var entries = new List<UpdateManifestEntry>();

        if (latestRelease.Assets is not null) {
            foreach (var asset in latestRelease.Assets) {
                var name = asset.Name;
                if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var downloadUrl = asset.DownloadUrl ?? asset.BrowserDownloadUrl;
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