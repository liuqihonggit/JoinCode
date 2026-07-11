namespace IO.Services;

[Register]
public sealed partial class ReleaseNotesService : IReleaseNotesService
{
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeProvider _timeProvider;

    private IReadOnlyList<ReleaseInfo>? _cachedReleases;
    private DateTimeOffset _cacheTimestamp;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public ReleaseNotesService(HttpClient httpClient, string repoOwner = "jcc", string repoName = "JoinCode",
        TimeSpan? requestTimeout = null, TimeSpan? cacheDuration = null, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _repoOwner = repoOwner;
        _repoName = repoName;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
        _cacheDuration = cacheDuration ?? TimeSpan.FromHours(1);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ReleaseInfo>> GetRecentReleasesAsync(int count = 5, CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedReleases != null && _timeProvider.GetUtcNow() - _cacheTimestamp < _cacheDuration)
                return _cachedReleases.Count <= count ? _cachedReleases : _cachedReleases.Take(count).ToList();
        }
        finally
        {
            _cacheLock.Release();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_requestTimeout);

        try
        {
            var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases?per_page={count}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "JoinCode");

            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            var releases = new List<ReleaseInfo>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var tagName = element.GetProperty("tag_name").GetString() ?? "unknown";
                var body = element.GetProperty("body").GetString() ?? "";
                var publishedAt = element.GetProperty("published_at").GetDateTime();

                if (tagName.StartsWith('v'))
                    tagName = tagName[1..];

                releases.Add(new ReleaseInfo
                {
                    Version = tagName,
                    Notes = StringTruncator.Truncate(body, 503),
                    PublishedAt = publishedAt
                });
            }

            var result = releases.AsReadOnly();

            await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _cachedReleases = result;
                _cacheTimestamp = _timeProvider.GetUtcNow();
            }
            finally
            {
                _cacheLock.Release();
            }

            return result;
        }
        catch
        {
            await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_cachedReleases != null)
                    return _cachedReleases.Count <= count ? _cachedReleases : _cachedReleases.Take(count).ToList();
            }
            finally
            {
                _cacheLock.Release();
            }

            return [];
        }
    }

}
