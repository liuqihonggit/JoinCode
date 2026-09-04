namespace IO.Services.Update;

/// <summary>
/// HTTP API 更新源 — 动态端点 /api/version/check + /api/download/{version}
/// 支持服务端动态逻辑（灰度发布、下载统计、多渠道分发）
/// > ADR: 0064
/// </summary>
public sealed class HttpApiUpdateSource : IUpdateSource
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;
    private readonly ILogger<HttpApiUpdateSource>? _logger;

    public HttpApiUpdateSource(HttpClient httpClient, string apiBaseUrl, ILogger<HttpApiUpdateSource>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiBaseUrl = apiBaseUrl.TrimEnd('/') ?? throw new ArgumentNullException(nameof(apiBaseUrl));
        _logger = logger;
    }

    public UpdateSourceType Type => UpdateSourceType.HttpApi;

    public async Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default)
    {
        try
        {
            _logger?.LogDebug("HttpApiUpdateSource: 检查版本 {Url}", _apiBaseUrl);

            var url = $"{_apiBaseUrl}/api/version/check";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("User-Agent", BrandConstants.ProductName);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return StaticFileUpdateSource.ParseManifest(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "HttpApiUpdateSource: 检查版本失败 {Url}", _apiBaseUrl);
            return null;
        }
    }

    public async Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var url = $"{_apiBaseUrl}/api/download/{Uri.EscapeDataString(entry.Version)}";
        _logger?.LogDebug("HttpApiUpdateSource: 下载 {Url}", url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", BrandConstants.ProductName);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }
}
