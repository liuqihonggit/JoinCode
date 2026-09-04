namespace IO.Services.Update;

/// <summary>
/// Gitea Release 镜像更新源 — 从 Gitea API 拉取 Release，转换为 UpdateManifest
/// Gitea API 格式: GET /repos/:owner/:repo/releases → [{ tag_name, assets: [{ name, download_url, size }] }]
/// > ADR: 0064
/// </summary>
public sealed class GiteaMirrorUpdateSource : GitHostMirrorUpdateSourceBase
{
    public GiteaMirrorUpdateSource(HttpClient httpClient, string mirrorBaseUrl, ILogger<GiteaMirrorUpdateSource>? logger = null)
        : base(httpClient, mirrorBaseUrl, logger)
    {
    }

    public override UpdateSourceType Type => UpdateSourceType.GiteaMirror;

    protected override string GetLatestReleaseUrl() => $"{MirrorBaseUrl}/releases";

    protected override UpdateManifest ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Gitea /releases 返回数组，取第一个（最新）
        var latestRelease = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().FirstOrDefault()
            : root;

        if (latestRelease.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("Gitea releases 为空");

        var tagName = latestRelease.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("Gitea release 缺少 tag_name");

        var version = ExtractVersion(tagName);
        var publishedAt = latestRelease.TryGetProperty("created_at", out var createdEl) ? createdEl.GetDateTimeOffset() : DateTimeOffset.MinValue;
        var body = latestRelease.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

        var entries = new List<UpdateManifestEntry>();

        if (latestRelease.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsEl.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var downloadUrl = asset.TryGetProperty("download_url", out var urlEl)
                    ? urlEl.GetString()
                    : asset.TryGetProperty("browser_download_url", out var fallbackUrlEl) ? fallbackUrlEl.GetString() : null;
                if (downloadUrl is null) continue;

                var size = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;

                entries.Add(new UpdateManifestEntry
                {
                    Version = version,
                    DownloadUrl = downloadUrl,
                    Sha256 = "",
                    SizeBytes = size,
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
