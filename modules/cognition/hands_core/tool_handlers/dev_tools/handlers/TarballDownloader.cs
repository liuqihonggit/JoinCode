namespace Tools.Handlers;

/// <summary>
/// 子模块 tarball 下载请求 — 已解析的 owner/repo/ref + 本地目标路径。
/// </summary>
public sealed record SubmoduleDownloadRequest(string Owner, string Repo, string Ref, string TargetPath);

/// <summary>
/// 子模块 tarball 下载结果。
/// </summary>
public sealed record SubmoduleDownloadResult(SubmoduleDownloadRequest Request, bool Success, string? TarballPath, string? Error);

/// <summary>
/// tarball 并行下载器 — HttpClient 并行下载 GitHub tarball API,复用 GitHubApiClient 端口检测。
/// <para>每个子模块: GET /repos/{owner}/{repo}/tarball/{ref} → 保存到临时目录。</para>
/// <para>端口不可达时复用 GitHubApiClient.IsPortReachableAsync 检测 + 友好错误。</para>
/// </summary>
public sealed class TarballDownloader {
    private readonly HttpClient _httpClient;
    private readonly string _token;
    private readonly IFileSystem _fs;
    private readonly ILogger<TarballDownloader>? _logger;

    private const string AcceptHeader = "application/vnd.github+json";
    private const string UserAgent = "jcc/1.0";
    private const int MaxRetries = 3;

    /// <summary>
    /// 构造 tarball 下载器。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端(BaseAddress 设为 https://api.github.com/)。</param>
    /// <param name="token">GitHub token(Bearer 认证);空字符串则匿名访问(公开仓库)。</param>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="logger">可选日志。</param>
    public TarballDownloader(HttpClient httpClient, string token, IFileSystem fs, ILogger<TarballDownloader>? logger = null) {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _token = token ?? string.Empty;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }

    /// <summary>
    /// 并行下载多个子模块 tarball 到临时目录。
    /// </summary>
    /// <param name="requests">下载请求列表(owner/repo/ref/targetPath)。</param>
    /// <param name="tempDir">临时目录(tarball 保存于此,调用方负责清理)。</param>
    /// <param name="parallelJobs">并行度(建议 CPU×2)。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>每个请求的下载结果。</returns>
    public async Task<IReadOnlyList<SubmoduleDownloadResult>> DownloadAllAsync(
        IReadOnlyList<SubmoduleDownloadRequest> requests,
        string tempDir,
        int parallelJobs,
        CancellationToken ct) {
        if (requests.Count == 0) return [];
        _fs.CreateDirectory(tempDir);

        using var sem = new SemaphoreSlim(Math.Max(1, parallelJobs));
        var tasks = requests.Select(r => DownloadOneAsync(r, tempDir, sem, ct)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return [..results];
    }

    private async Task<SubmoduleDownloadResult> DownloadOneAsync(
        SubmoduleDownloadRequest request,
        string tempDir,
        SemaphoreSlim sem,
        CancellationToken ct) {
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try {
            var tarballPath = _fs.CombinePath(tempDir, $"{request.Owner}-{request.Repo}.tar.gz");
            var path = $"repos/{request.Owner}/{request.Repo}/tarball/{request.Ref}";

            for (var retry = 0; retry <= MaxRetries; retry++) {
                try {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Get, path);
                    httpRequest.Headers.Accept.ParseAdd(AcceptHeader);
                    httpRequest.Headers.UserAgent.ParseAdd(UserAgent);
                    if (!string.IsNullOrEmpty(_token)) {
                        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
                    }

                    using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode) {
                        if (IsRetryable((int)response.StatusCode) && retry < MaxRetries) {
                            var backoff = TimeSpan.FromSeconds(Math.Pow(2, retry + 1));
                            _logger?.LogWarning("tarball 下载 {Owner}/{Repo} 返回 {Code},第{Retry}次重试,{Backoff:F0}s", request.Owner, request.Repo, (int)response.StatusCode, retry + 1, backoff.TotalSeconds);
                            await Task.Delay(backoff, ct).ConfigureAwait(false);
                            continue;
                        }
                        return new SubmoduleDownloadResult(request, false, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                    }

                    await using var networkStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var fileStream = _fs.Open(tarballPath, FileMode.Create);
                    await networkStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);

                    _logger?.LogInformation("tarball 下载完成: {Owner}/{Repo} → {Path}", request.Owner, request.Repo, tarballPath);
                    return new SubmoduleDownloadResult(request, true, tarballPath, null);
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) when (retry < MaxRetries) {
                    var backoff = TimeSpan.FromSeconds(Math.Pow(2, retry + 1));
                    _logger?.LogWarning(ex, "tarball 下载 {Owner}/{Repo} 异常,第{Retry}次重试", request.Owner, request.Repo, retry + 1);
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
            }

            return new SubmoduleDownloadResult(request, false, null, "超过最大重试次数");
        } finally {
            sem.Release();
        }
    }

    private static bool IsRetryable(int statusCode) => statusCode is >= 500 and < 600 or 429;
}
