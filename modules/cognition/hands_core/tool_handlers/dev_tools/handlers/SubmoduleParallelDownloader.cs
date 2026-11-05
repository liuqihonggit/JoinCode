namespace Tools.Handlers;

/// <summary>
/// 子模块更新结果。
/// </summary>
public sealed record SubmoduleUpdateResult(bool Success, string Strategy, string Message, IReadOnlyList<SubmoduleDownloadResult> Downloads);

/// <summary>
/// 自适应子模块并行下载器 — 探测延迟 → 策略选择(git --jobs / tarball API) → 下载 → 解压 → 补 git 元数据。
/// <para>低延迟(端口可达 + 连接 &lt; 阈值): git submodule update --init --jobs=CPU×2(原生 git,带元数据)。</para>
/// <para>高延迟(端口不可达 或 连接 ≥ 阈值): tarball API 并行下载 → 解压 → git submodule update --init --force 补元数据。</para>
/// <para>复用 GitHubApiClient.IsPortReachableAsync 端口检测; TempDirScope 自动清理临时压缩包。</para>
/// </summary>
public sealed class SubmoduleParallelDownloader {
    private readonly HttpClient _httpClient;
    private readonly string _token;
    private readonly IFileSystem _fs;
    private readonly IGitCommandRunner _gitRunner;
    private readonly ILogger<SubmoduleParallelDownloader>? _logger;

    private static readonly TimeSpan LatencyThreshold = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 构造自适应子模块下载器。
    /// </summary>
    public SubmoduleParallelDownloader(
        HttpClient httpClient,
        string token,
        IFileSystem fs,
        IGitCommandRunner gitRunner,
        ILogger<SubmoduleParallelDownloader>? logger = null) {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _token = token ?? string.Empty;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
        _logger = logger;
    }

    /// <summary>
    /// 自适应更新子模块 — 探测延迟,自动选择 git --jobs 或 tarball API 方案。
    /// </summary>
    /// <param name="workingDir">Git 仓库根目录(含 .gitmodules)。</param>
    /// <param name="parallelJobs">并行度(建议 CPU×2)。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<SubmoduleUpdateResult> UpdateAsync(string workingDir, int parallelJobs, CancellationToken ct) {
        var (reachable, latencyMs) = await ProbeGitHubLatencyAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("GitHub 延迟探测: 可达={Reachable}, 延迟={LatencyMs}ms", reachable, latencyMs);

        if (reachable && latencyMs < LatencyThreshold.TotalMilliseconds) {
            return await UpdateViaGitAsync(workingDir, parallelJobs, ct).ConfigureAwait(false);
        }

        _logger?.LogInformation("延迟 {LatencyMs}ms 超过阈值 {Threshold}ms,切换 tarball API 方案", latencyMs, LatencyThreshold.TotalMilliseconds);
        return await UpdateViaTarballAsync(workingDir, parallelJobs, ct).ConfigureAwait(false);
    }

    private async Task<SubmoduleUpdateResult> UpdateViaGitAsync(string workingDir, int parallelJobs, CancellationToken ct) {
        var args = $"{GitSubCommand.Submodule.ToValue()} update --init --jobs={parallelJobs}";
        var result = await _gitRunner.ExecuteAsync(args, workingDir, ct).ConfigureAwait(false);
        return new SubmoduleUpdateResult(
            result.Success,
            "git-jobs",
            result.Success ? $"git submodule update 成功 (jobs={parallelJobs})" : result.Error,
            []);
    }

    private async Task<SubmoduleUpdateResult> UpdateViaTarballAsync(string workingDir, int parallelJobs, CancellationToken ct) {
        var gitmodulesPath = _fs.CombinePath(workingDir, ".gitmodules");
        if (!_fs.FileExists(gitmodulesPath)) {
            return new SubmoduleUpdateResult(false, "tarball", ".gitmodules 不存在", []);
        }

        var content = await _fs.ReadAllTextAsync(gitmodulesPath, ct).ConfigureAwait(false);
        var entries = SubmoduleManifest.Parse(content);
        if (entries.Count == 0) {
            return new SubmoduleUpdateResult(true, "tarball", "无子模块需要更新", []);
        }

        var requests = BuildDownloadRequests(entries);
        if (requests.Count == 0) {
            return new SubmoduleUpdateResult(false, "tarball", "无法解析子模块 URL", []);
        }

        await using var tmp = TempDirScope.Create(_fs, "submodule_tarball_");
        var downloader = new TarballDownloader(_httpClient, _token, _fs, logger: null);
        var downloads = await downloader.DownloadAllAsync(requests, tmp.Path, parallelJobs, ct).ConfigureAwait(false);

        var failed = downloads.Where(d => !d.Success).ToList();
        if (failed.Count > 0) {
            return new SubmoduleUpdateResult(false, "tarball", $"{failed.Count}/{downloads.Count} 子模块下载失败", downloads);
        }

        foreach (var d in downloads) {
            if (d.TarballPath is not string tarballPath) continue;
            var targetPath = _fs.CombinePath(workingDir, d.Request.TargetPath);
            using var tarStream = _fs.OpenRead(tarballPath);
            await TarExtractor.ExtractAsync(tarStream, targetPath, _fs, ct).ConfigureAwait(false);
        }

        var fixupResult = await _gitRunner.ExecuteAsync("submodule update --init --force", workingDir, ct).ConfigureAwait(false);
        return new SubmoduleUpdateResult(
            fixupResult.Success,
            "tarball",
            fixupResult.Success ? $"tarball 方案成功: {downloads.Count} 子模块并行下载+解压+补元数据" : $"补 git 元数据失败: {fixupResult.Error}",
            downloads);
    }

    private static List<SubmoduleDownloadRequest> BuildDownloadRequests(IReadOnlyList<SubmoduleEntry> entries) {
        var requests = new List<SubmoduleDownloadRequest>();
        foreach (var e in entries) {
            if (ParseGitHubUrl(e.Url) is (string owner, string repo)) {
                requests.Add(new SubmoduleDownloadRequest(owner, repo, e.Branch ?? "main", e.Path));
            }
        }
        return requests;
    }

    /// <summary>
    /// 解析 GitHub URL → (owner, repo)。支持 git@github.com:owner/repo.git 和 https://github.com/owner/repo.git。
    /// </summary>
    internal static (string owner, string repo)? ParseGitHubUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string path;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)) {
            var uri = new Uri(url);
            path = uri.AbsolutePath.TrimStart('/');
        } else {
            var colonIdx = url.IndexOf(':');
            if (colonIdx < 0) return null;
            path = url[(colonIdx + 1)..];
        }
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1])) return null;
        return (parts[0], parts[1]);
    }

    /// <summary>
    /// 探测 GitHub API 延迟 — TcpClient 连接 api.github.com:443,返回(可达, 延迟ms)。
    /// </summary>
    private static async Task<(bool reachable, long latencyMs)> ProbeGitHubLatencyAsync(CancellationToken ct) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            using var timeoutCts = new CancellationTokenSource(ProbeTimeout);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using var tcp = new System.Net.Sockets.TcpClient();
            await tcp.ConnectAsync("api.github.com", 443, cts.Token).ConfigureAwait(false);
            sw.Stop();
            return (true, sw.ElapsedMilliseconds);
        } catch {
            sw.Stop();
            return (false, sw.ElapsedMilliseconds);
        }
    }
}
