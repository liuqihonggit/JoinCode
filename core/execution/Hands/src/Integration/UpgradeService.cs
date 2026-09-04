namespace IO.Services;

[Register(typeof(IUpgradeService), ServiceLifetime.Singleton)]
public sealed partial class UpgradeService : ServiceEntity, IUpgradeService
{
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly ILogger<UpgradeService>? _logger;
    private Version? _cachedLatest;

    public UpgradeService(HttpClient httpClient, string? repoOwner = null, string? repoName = null, ILogger<UpgradeService>? logger = null)
    {
        _httpClient = httpClient;
        _repoOwner = repoOwner ?? JccEndpointsResolver.RepoOwner;
        _repoName = repoName ?? JccEndpointsResolver.RepoName;
        _logger = logger;
    }

    public Version GetCurrentVersion()
    {
        return typeof(UpgradeService).Assembly.GetName().Version ?? new Version(0, 1, 0);
    }

    public async Task<Version?> GetLatestVersionAsync(CancellationToken ct = default)
    {
        if (_cachedLatest != null) return _cachedLatest;

        try
        {
            var url = $"{JccEndpointsResolver.GitHubApiBase}/repos/{_repoOwner}/{_repoName}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "JoinCode");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var tagName = doc.RootElement.GetProperty("tag_name").GetString();

            if (tagName != null && tagName.StartsWith('v'))
                tagName = tagName[1..];

            if (Version.TryParse(tagName, out var version))
            {
                _cachedLatest = version;
                return version;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "UpgradeService: 获取最新版本失败");
        }

        return null;
    }

    public async Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default)
    {
        var latest = await GetLatestVersionAsync(ct).ConfigureAwait(false);
        return latest != null && latest > GetCurrentVersion();
    }

    /// <summary>
    /// 获取最新版本的清单条目 — TODO: ADR 0064 待实现，需注入 IUpdateSource
    /// </summary>
    public Task<UpdateManifestEntry?> GetUpdateEntryAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("ADR 0064: GetUpdateEntryAsync 待实现，需注入 IUpdateSource");
    }

    /// <summary>
    /// 下载更新到临时目录 — TODO: ADR 0064 待实现，需用 RangeDownloader + SHA256 校验
    /// </summary>
    public Task<UpdateResult> DownloadUpdateAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        throw new NotImplementedException("ADR 0064: DownloadUpdateAsync 待实现，需用 RangeDownloader + SHA256 校验");
    }

    /// <summary>
    /// 应用更新 — 原子替换当前 exe — TODO: ADR 0064 待实现，需备份→替换→回滚逻辑
    /// </summary>
    public Task<UpdateResult> ApplyUpdateAsync(string downloadedExePath, CancellationToken ct = default)
    {
        throw new NotImplementedException("ADR 0064: ApplyUpdateAsync 待实现，需备份→替换→回滚逻辑");
    }
}
