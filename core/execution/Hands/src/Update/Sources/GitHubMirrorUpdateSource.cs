namespace IO.Services.Update;

/// <summary>
/// GitHub Release 镜像更新源 — 镜像 GitHub API 响应格式，解决国内访问慢
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
        _mirrorBaseUrl = mirrorBaseUrl ?? throw new ArgumentNullException(nameof(mirrorBaseUrl));
        _logger = logger;
    }

    public UpdateSourceType Type => UpdateSourceType.GitHubMirror;

    public Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("ADR 0064: GitHubMirrorUpdateSource.GetManifestAsync 待实现");
    }

    public Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        throw new NotImplementedException("ADR 0064: GitHubMirrorUpdateSource.DownloadAsync 待实现");
    }
}
