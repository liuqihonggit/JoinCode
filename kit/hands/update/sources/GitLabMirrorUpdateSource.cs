namespace IO.Services.Update;

/// <summary>
/// GitLab Release 镜像更新源 — 从 GitLab API 拉取 Release，转换为 UpdateManifest
/// GitLab API 格式: GET /projects/:id/releases → [{ tag_name, assets: { links: [{ name, direct_asset_url }] } }]
/// > ADR: 0064
/// </summary>
public sealed class GitLabMirrorUpdateSource : GitHostMirrorUpdateSourceBase
{
    /// <summary>
    /// 构造 GitLab 镜像更新源
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="mirrorBaseUrl">镜像基础 URL</param>
    /// <param name="logger">日志器（可选）</param>
    public GitLabMirrorUpdateSource(HttpClient httpClient, string mirrorBaseUrl, ILogger<GitLabMirrorUpdateSource>? logger = null)
        : base(httpClient, mirrorBaseUrl, logger)
    {
    }

    /// <summary>
    /// 更新源类型 — GitLabMirror
    /// </summary>
    public override UpdateSourceType Type => UpdateSourceType.GitLabMirror;

    /// <summary>
    /// 获取最新 Release 的 API URL — GitLab /releases 端点
    /// </summary>
    /// <returns>最新 Release 的 API URL</returns>
    protected override string GetLatestReleaseUrl() => $"{MirrorBaseUrl}/releases";

    /// <summary>
    /// 解析 GitLab Release JSON 为更新清单
    /// </summary>
    /// <param name="json">Release JSON 文本</param>
    /// <returns>更新清单</returns>
    protected override UpdateManifest ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // GitLab /releases 返回数组，取第一个（最新）
        var latestRelease = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().FirstOrDefault()
            : root;

        if (latestRelease.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("GitLab releases 为空");

        var tagName = latestRelease.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("GitLab release 缺少 tag_name");

        var version = ExtractVersion(tagName);
        var publishedAt = latestRelease.TryGetProperty("released_at", out var relEl) ? relEl.GetDateTimeOffset() : DateTimeOffset.MinValue;
        var body = latestRelease.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;

        var entries = new List<UpdateManifestEntry>();

        if (latestRelease.TryGetProperty("assets", out var assetsEl)
            && assetsEl.TryGetProperty("links", out var linksEl)
            && linksEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in linksEl.EnumerateArray())
            {
                var name = link.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var downloadUrl = link.TryGetProperty("direct_asset_url", out var urlEl)
                    ? urlEl.GetString()
                    : link.TryGetProperty("url", out var fallbackUrlEl) ? fallbackUrlEl.GetString() : null;
                if (downloadUrl is null) continue;

                entries.Add(new UpdateManifestEntry
                {
                    Version = version,
                    DownloadUrl = downloadUrl,
                    Sha256 = "",
                    SizeBytes = 0,
                    ReleaseNotes = body,
                    PublishedAt = publishedAt,
                });
            }
        }

        return new UpdateManifest
        {
            LatestVersion = version,
            Channel = "stable",
            Releases = entries.AsReadOnly()
        };
    }
}
