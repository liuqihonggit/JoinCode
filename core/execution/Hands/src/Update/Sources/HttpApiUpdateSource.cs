namespace IO.Services.Update;

/// <summary>
/// HTTP API 更新源 — 动态端点 /api/version/check + /api/download/{version}
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
        _apiBaseUrl = apiBaseUrl ?? throw new ArgumentNullException(nameof(apiBaseUrl));
        _logger = logger;
    }

    public UpdateSourceType Type => UpdateSourceType.HttpApi;

    public Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("ADR 0064: HttpApiUpdateSource.GetManifestAsync 待实现");
    }

    public Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        throw new NotImplementedException("ADR 0064: HttpApiUpdateSource.DownloadAsync 待实现");
    }
}
