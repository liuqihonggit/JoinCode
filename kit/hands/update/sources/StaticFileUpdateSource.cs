namespace IO.Services.Update;

/// <summary>
/// 静态文件更新源 — 从 HTTP 服务器拉取 manifest.json，下载 exe 二进制
/// 服务器只托管静态文件，无服务端逻辑（nginx/Python http.server/GitHub Pages 均可）
/// > ADR: 0064
/// </summary>
public sealed class StaticFileUpdateSource : IUpdateSource
{
    private readonly HttpClient _httpClient;
    private readonly string _manifestUrl;
    private readonly ILogger<StaticFileUpdateSource>? _logger;

    /// <summary>
    /// 构造静态文件更新源
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="manifestUrl">清单 URL（manifest.json 的完整地址）</param>
    /// <param name="logger">日志器</param>
    public StaticFileUpdateSource(HttpClient httpClient, string manifestUrl, ILogger<StaticFileUpdateSource>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _manifestUrl = manifestUrl ?? throw new ArgumentNullException(nameof(manifestUrl));
        _logger = logger;
    }

    /// <inheritdoc/>
    public UpdateSourceType Type => UpdateSourceType.Static;

    /// <inheritdoc/>
    public async Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default)
    {
        try
        {
            _logger?.LogDebug("StaticFileUpdateSource: 拉取清单 {Url}", _manifestUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, _manifestUrl);
            request.Headers.Add("User-Agent", BrandConstants.ProductName);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseManifest(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "StaticFileUpdateSource: 拉取清单失败 {Url}", _manifestUrl);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var downloadUrl = ResolveDownloadUrl(entry.DownloadUrl);
        _logger?.LogDebug("StaticFileUpdateSource: 下载 {Url}", downloadUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.Add("User-Agent", BrandConstants.ProductName);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析下载 URL — 相对 URL 解析为相对于清单地址的绝对 URL
    /// </summary>
    private string ResolveDownloadUrl(string downloadUrl)
    {
        if (Uri.IsWellFormedUriString(downloadUrl, UriKind.Absolute))
            return downloadUrl;

        var baseUri = new Uri(_manifestUrl);
        return new Uri(baseUri, downloadUrl).ToString();
    }

    /// <summary>
    /// 手动解析 manifest.json（避免新增 JsonContext，AOT 友好）
    /// 供 LocalFileUpdateSource 等其他源复用
    /// </summary>
    internal static UpdateManifest ParseManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var latestVersion = root.GetProperty("latestVersion").GetString()
            ?? throw new InvalidOperationException("manifest.json 缺少 latestVersion");

        var channel = root.TryGetProperty("channel", out var channelEl) ? channelEl.GetString() ?? "stable" : "stable";

        var releases = new List<UpdateManifestEntry>();
        if (root.TryGetProperty("releases", out var releasesEl) && releasesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in releasesEl.EnumerateArray())
            {
                releases.Add(ParseEntry(entry));
            }
        }

        return new UpdateManifest
        {
            LatestVersion = latestVersion,
            Channel = channel,
            Releases = releases.AsReadOnly()
        };
    }

    private static UpdateManifestEntry ParseEntry(JsonElement element)
    {
        var version = element.GetProperty("version").GetString()
            ?? throw new InvalidOperationException("release 条目缺少 version");
        var downloadUrl = element.GetProperty("downloadUrl").GetString()
            ?? throw new InvalidOperationException("release 条目缺少 downloadUrl");
        var sha256 = element.GetProperty("sha256").GetString()
            ?? throw new InvalidOperationException("release 条目缺少 sha256");

        return new UpdateManifestEntry
        {
            Version = version,
            DownloadUrl = downloadUrl,
            Sha256 = sha256,
            SizeBytes = element.TryGetProperty("sizeBytes", out var sizeEl) ? sizeEl.GetInt64() : 0,
            ReleaseNotes = element.TryGetProperty("releaseNotes", out var notesEl) ? notesEl.GetString() : null,
            PublishedAt = element.TryGetProperty("publishedAt", out var pubEl) ? pubEl.GetDateTimeOffset() : DateTimeOffset.MinValue,
            MinUpgradeFrom = element.TryGetProperty("minUpgradeFrom", out var minEl) ? minEl.GetString() : null,
        };
    }
}
