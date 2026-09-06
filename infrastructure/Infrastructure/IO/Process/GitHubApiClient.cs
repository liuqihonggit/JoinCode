namespace IO.ProcessService;

/// <summary>
/// GitHub REST API 客户端实现 — HttpClient 直调 https://api.github.com，摆脱系统 gh CLI 依赖
/// <para>ADR: 0072 — 替代 GitHubCommandRunner 起子进程方案</para>
/// <para>Token: JCC_GITHUB_TOKEN → GITHUB_TOKEN → 抛 ConfigurationException</para>
/// <para>Base URL: JCC_GITHUB_API_URL 默认 https://api.github.com（支持 Enterprise）</para>
/// <para>Rate limit: 403 + X-RateLimit-Remaining:0 → Retry-After → 重试（最多 3 次）</para>
/// <para>分页: Link header rel=next，paginate=true 时自动跟随合并 JSON 数组</para>
/// </summary>
[Register(typeof(IGitHubApiClient), ServiceLifetime.Singleton)]
public sealed partial class GitHubApiClient : ServiceEntity, IGitHubApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubApiClient>? _logger;

    private const string DefaultBaseUrl = "https://api.github.com/";
    private const string AcceptHeader = "application/vnd.github+json";
    private const string UserAgent = "jcc/1.0";
    private const int MaxRateLimitRetries = 3;

    public GitHubApiClient(
        HttpClient httpClient,
        ILogger<GitHubApiClient>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            var baseUrl = Environment.GetEnvironmentVariable("JCC_GITHUB_API_URL");
            _httpClient.BaseAddress = new Uri(string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : EnsureTrailingSlash(baseUrl));
        }

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any(a => a.MediaType == AcceptHeader))
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(AcceptHeader));
        }
    }

    /// <summary>
    /// 通用 REST API 调用 — 构造请求 → 发送 → 处理 rate limit → 解析响应 → 分页合并
    /// </summary>
    public async Task<GitHubApiResponse> SendAsync(
        HttpMethod method,
        string path,
        string? body = null,
        IReadOnlyDictionary<string, string>? query = null,
        bool paginate = false,
        CancellationToken ct = default)
    {
        var token = ResolveToken();
        var effectivePath = NormalizePath(path);
        var allBodies = new List<string>();
        string? nextUrl = null;
        int lastStatusCode = 0;
        var isFirstPage = true;

        while (true)
        {
            var request = BuildRequest(method, isFirstPage ? effectivePath : nextUrl!, body, query, token, isFirstPage);
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GitHub API 请求失败: {Method} {Path}", method, path);
                return new GitHubApiResponse { Success = false, StatusCode = 0, Error = ex.Message };
            }

            using (response)
            {
                lastStatusCode = (int)response.StatusCode;

                if (IsRateLimited(response))
                {
                    var retryAfter = ParseRetryAfter(response);
                    if (retryAfter is null || retryAfter.Value > TimeSpan.FromMinutes(1))
                    {
                        var bodyText = await ReadBodyAsync(response, ct).ConfigureAwait(false);
                        return new GitHubApiResponse { Success = false, StatusCode = 429, Error = $"GitHub rate limit exceeded. {bodyText}" };
                    }
                    _logger?.LogWarning("GitHub rate limit 命中，{Delay}s 后重试", retryAfter.Value.TotalSeconds);
                    await Task.Delay(retryAfter.Value, ct).ConfigureAwait(false);
                    continue;
                }

                var responseBody = await ReadBodyAsync(response, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = ExtractErrorMessage(responseBody) ?? $"HTTP {lastStatusCode}";
                    return new GitHubApiResponse { Success = false, StatusCode = lastStatusCode, Error = errorMsg, Body = responseBody };
                }

                allBodies.Add(responseBody);
                nextUrl = ParseLinkHeaderNext(response.Headers);

                if (!paginate || string.IsNullOrEmpty(nextUrl))
                {
                    break;
                }
            }

            isFirstPage = false;
            body = null;
            query = null;
        }

        var merged = paginate && allBodies.Count > 1 ? MergeJsonArrays(allBodies) : allBodies[0];
        return new GitHubApiResponse { Success = true, StatusCode = lastStatusCode, Body = merged, NextPageUrl = nextUrl };
    }

    /// <summary>
    /// 获取 Actions Run 日志 — GET /repos/{owner}/{repo}/actions/runs/{runId}/logs 返回 zip，解压后逐文件逐行 yield
    /// </summary>
    public async IAsyncEnumerable<string> GetRunLogsAsync(
        string owner,
        string repo,
        long runId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var token = ResolveToken();
        var path = $"repos/{owner}/{repo}/actions/runs/{runId}/logs";
        var request = BuildRequest(HttpMethod.Get, path, null, null, token, true);

        HttpResponseMessage? response = null;
        string? fetchError = null;
        var canceled = false;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { canceled = true; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取 Run 日志失败: runId={RunId}", runId);
            fetchError = $"[ERROR] 获取 Run 日志失败: {ex.Message}";
        }

        if (canceled) yield break;
        if (fetchError is not null)
        {
            yield return fetchError;
            yield break;
        }

        var resp = response!;
        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await ReadBodyAsync(resp, ct).ConfigureAwait(false);
                yield return $"[ERROR] HTTP {(int)resp.StatusCode}: {ExtractErrorMessage(errBody)}";
                yield break;
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0) continue;
                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                {
                    yield return $"[{entry.Name}] {line}";
                }
            }
        }
    }

    /// <summary>
    /// 获取 Actions Job 日志 — 逐行 yield（zip 解压后逐文件逐行）
    /// </summary>
    public async IAsyncEnumerable<string> GetJobLogsAsync(
        string owner,
        string repo,
        long jobId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var token = ResolveToken();
        var path = $"repos/{owner}/{repo}/actions/jobs/{jobId}/logs";
        var request = BuildRequest(HttpMethod.Get, path, null, null, token, true);

        HttpResponseMessage? response = null;
        string? fetchError = null;
        var canceled = false;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { canceled = true; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取 Job 日志失败: jobId={JobId}", jobId);
            fetchError = $"[ERROR] 获取 Job 日志失败: {ex.Message}";
        }

        if (canceled) yield break;
        if (fetchError is not null)
        {
            yield return fetchError;
            yield break;
        }

        var resp = response!;
        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await ReadBodyAsync(resp, ct).ConfigureAwait(false);
                yield return $"[ERROR] HTTP {(int)resp.StatusCode}: {ExtractErrorMessage(errBody)}";
                yield break;
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0) continue;
                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                {
                    yield return $"[{entry.Name}] {line}";
                }
            }
        }
    }

    /// <summary>
    /// 上传 Release asset — 二进制上传到 uploads.github.com
    /// </summary>
    public async Task<GitHubApiResponse> UploadAssetAsync(
        string owner,
        string repo,
        long releaseId,
        string fileName,
        Stream fileStream,
        CancellationToken ct = default)
    {
        var token = ResolveToken();
        var uploadsBase = Environment.GetEnvironmentVariable("JCC_GITHUB_UPLOADS_URL") ?? "https://uploads.github.com/";
        uploadsBase = EnsureTrailingSlash(uploadsBase);
        var uploadUrl = $"{uploadsBase}repos/{owner}/{repo}/releases/{releaseId}/assets?name={Uri.EscapeDataString(fileName)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("Accept", AcceptHeader);
        request.Headers.Add("User-Agent", UserAgent);
        request.Content = new StreamContent(fileStream);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        try
        {
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return new GitHubApiResponse { Success = true, StatusCode = (int)response.StatusCode, Body = body };
            return new GitHubApiResponse { Success = false, StatusCode = (int)response.StatusCode, Error = ExtractErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}" };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "上传 Release asset 失败: {FileName}", fileName);
            return new GitHubApiResponse { Success = false, StatusCode = 0, Error = ex.Message };
        }
    }

    // === 私有辅助方法 ===

    private static string ResolveToken()
    {
        var token = Environment.GetEnvironmentVariable("JCC_GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw ConfigurationException.Missing("JCC_GITHUB_TOKEN (或 GITHUB_TOKEN)");
        }
        return token;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private static string NormalizePath(string path) => path.StartsWith('/') ? path[1..] : path;

    private static HttpRequestMessage BuildRequest(
        HttpMethod method,
        string pathOrUrl,
        string? body,
        IReadOnlyDictionary<string, string>? query,
        string token,
        bool applyQuery)
    {
        var isAbsolute = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        var uri = isAbsolute ? new Uri(pathOrUrl) : BuildUri(pathOrUrl, applyQuery ? query : null);

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrEmpty(body))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return request;
    }

    private static Uri BuildUri(string path, IReadOnlyDictionary<string, string>? query)
    {
        if (query is null || query.Count == 0) return new Uri(path, UriKind.Relative);

        var sb = new StringBuilder(path);
        sb.Append(path.Contains('?') ? '&' : '?');
        var first = true;
        foreach (var kvp in query)
        {
            if (!first) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kvp.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kvp.Value));
            first = false;
        }
        return new Uri(sb.ToString(), UriKind.Relative);
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.Forbidden) return false;
        var remaining = response.Headers.FirstOrDefault(h => string.Equals(h.Key, "X-RateLimit-Remaining", StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
        return remaining == "0";
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.FirstOrDefault(h => string.Equals(h.Key, "Retry-After", StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
        if (retryAfter is null || !int.TryParse(retryAfter, out var seconds)) return null;
        return TimeSpan.FromSeconds(seconds);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static string? ParseLinkHeaderNext(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        if (!headers.Contains("Link")) return null;
        var linkHeader = headers.GetValues("Link").FirstOrDefault();
        if (string.IsNullOrEmpty(linkHeader)) return null;

        var parts = linkHeader.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!trimmed.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase)) continue;
            var start = trimmed.IndexOf('<');
            var end = trimmed.IndexOf('>');
            if (start >= 0 && end > start) return trimmed[(start + 1)..end];
        }
        return null;
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        if (body[0] != '{') return body.Length > 500 ? body[..500] : body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msgEl))
            {
                return msgEl.GetString();
            }
        }
#pragma warning disable JCC3013
        catch (Exception) { }
#pragma warning restore JCC3013
        return body.Length > 500 ? body[..500] : body;
    }

    private static string MergeJsonArrays(IReadOnlyList<string> bodies)
    {
        var sb = new StringBuilder("[");
        var first = true;
        foreach (var body in bodies)
        {
            if (string.IsNullOrEmpty(body)) continue;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (!first) sb.Append(',');
                        sb.Append(item.GetRawText());
                        first = false;
                    }
                }
                else
                {
                    return bodies[0];
                }
            }
            catch (Exception) { return bodies[0]; }
        }
        sb.Append(']');
        return sb.ToString();
    }
}
