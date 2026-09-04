namespace IO.Services.Update;

/// <summary>
/// GitHub Release 镜像更新源 — 镜像 GitHub API 响应格式，解决国内访问慢/超时
/// 从镜像服务器拉取 /releases/latest（GitHub API 格式），转换为 UpdateManifest
/// > ADR: 0064
/// </summary>
public sealed class GitHubMirrorUpdateSource : IUpdateSource
{
    private readonly HttpClient _httpClient;
    private readonly string _mirrorBaseUrl;
    private readonly ILogger<GitHubMirrorUpdateSource>? _logger;

    public GitHubMirrorUpdateSource(HttpClient httpClient, string mirrorBaseUrl, ILogger<GitHubMirrorUpdateSource>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _mirrorBaseUrl = mirrorBaseUrl.TrimEnd('/') ?? throw new ArgumentNullException(nameof(mirrorBaseUrl));
        _logger = logger;
    }

    public UpdateSourceType Type => UpdateSourceType.GitHubMirror;

    public async Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default)
    {
        try
        {
            _logger?.LogDebug("GitHubMirrorUpdateSource: 拉取最新 release {Url}", _mirrorBaseUrl);

            var url = $"{_mirrorBaseUrl}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", BrandConstants.ProductName);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseGitHubRelease(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "GitHubMirrorUpdateSource: 拉取失败 {Url}", _mirrorBaseUrl);
            return null;
        }
    }

    public async Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _logger?.LogDebug("GitHubMirrorUpdateSource: 下载 {Url}", entry.DownloadUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, entry.DownloadUrl);
        request.Headers.Add("User-Agent", BrandConstants.ProductName);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析 GitHub API release 响应 → UpdateManifest
    /// GitHub API 格式: { tag_name, body, published_at, assets: [{ name, browser_download_url, size }] }
    /// </summary>
    private static UpdateManifest ParseGitHubRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("GitHub release 缺少 tag_name");

        var version = tagName.StartsWith('v') ? tagName[1..] : tagName;
        var publishedAt = root.TryGetProperty("published_at", out var pubEl) ? pubEl.GetDateTimeOffset() : DateTimeOffset.MinValue;
        var body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

        var entries = new List<UpdateManifestEntry>();

        if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsEl.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                if (downloadUrl is null) continue;

                var size = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;

                entries.Add(new UpdateManifestEntry
                {
                    Version = version,
                    DownloadUrl = downloadUrl,
                    Sha256 = "", // GitHub Release 不提供 SHA256，跳过校验
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
