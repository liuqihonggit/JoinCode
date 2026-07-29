namespace IO.Services;

[Register]
public sealed partial class UpgradeService : IUpgradeService
{
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private Version? _cachedLatest;

    public UpgradeService(HttpClient httpClient, string repoOwner = "jcc", string repoName = "JoinCode")
    {
        _httpClient = httpClient;
        _repoOwner = repoOwner;
        _repoName = repoName;
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
            var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
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
            System.Diagnostics.Trace.WriteLine($"UpgradeService: failed to get latest version: {ex.Message}");
        }

        return null;
    }

    public async Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default)
    {
        var latest = await GetLatestVersionAsync(ct).ConfigureAwait(false);
        return latest != null && latest > GetCurrentVersion();
    }
}
