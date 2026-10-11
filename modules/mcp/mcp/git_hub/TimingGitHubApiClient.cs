namespace McpToolDispatch;

/// <summary>
/// IGitHubApiClient 装饰器 — 自动拦截网络请求耗时,写入 GhTimingTracker,可插拔
/// <para>可插拔: 无 tracker 时透传不记录 — 随时剥离计时层</para>
/// <para>管道位置: GitHubApiClient → CacheGitHubApiClient → <b>TimingGitHubApiClient</b> → handler</para>
/// <para>SendAsync 计网络耗时;GetRunLogsAsync/GetJobLogsAsync 透传(日志下载计时在 GitHubRunLogFilterRunner 中)</para>
/// </summary>
internal sealed class TimingGitHubApiClient : IGitHubApiClient {
    private readonly IGitHubApiClient _inner;

    /// <summary>创建计时装饰器,包装内层 IGitHubApiClient</summary>
    /// <param name="inner">被包装的内层客户端(真实实现或缓存装饰器)</param>
    public TimingGitHubApiClient(IGitHubApiClient inner) {
        _inner = inner;
    }

    /// <summary>通用 REST API 调用 — 自动计网络耗时,无 tracker 时透传</summary>
    public async Task<GitHubApiResponse> SendAsync(
        HttpMethod method, string path, string? body = null,
        IReadOnlyDictionary<string, string>? query = null,
        bool paginate = false, CancellationToken ct = default) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        if (tracker is null) return await _inner.SendAsync(method, path, body, query, paginate, ct).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        try {
            return await _inner.SendAsync(method, path, body, query, paginate, ct).ConfigureAwait(false);
        } finally {
            tracker.AddNetwork(Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>获取 Run 日志 — 透传(日志下载计时在 GitHubRunLogFilterRunner 中)</summary>
    public IAsyncEnumerable<string> GetRunLogsAsync(string owner, string repo, long runId, CancellationToken ct = default)
        => _inner.GetRunLogsAsync(owner, repo, runId, ct);

    /// <summary>获取 Job 日志 — 透传(日志下载计时在 GitHubRunLogFilterRunner 中)</summary>
    public IAsyncEnumerable<string> GetJobLogsAsync(string owner, string repo, long jobId, CancellationToken ct = default)
        => _inner.GetJobLogsAsync(owner, repo, jobId, ct);

    /// <summary>上传 Release asset — 自动计网络耗时</summary>
    public async Task<GitHubApiResponse> UploadAssetAsync(
        string owner, string repo, long releaseId, string fileName, Stream fileStream, CancellationToken ct = default) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        if (tracker is null) return await _inner.UploadAssetAsync(owner, repo, releaseId, fileName, fileStream, ct).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        try {
            return await _inner.UploadAssetAsync(owner, repo, releaseId, fileName, fileStream, ct).ConfigureAwait(false);
        } finally {
            tracker.AddNetwork(Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>上传 Issue/PR 附件 — 自动计网络耗时</summary>
    public async Task<GitHubApiResponse> UploadAttachmentAsync(
        long repositoryId, string fileName, Stream fileStream, CancellationToken ct = default) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        if (tracker is null) return await _inner.UploadAttachmentAsync(repositoryId, fileName, fileStream, ct).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        try {
            return await _inner.UploadAttachmentAsync(repositoryId, fileName, fileStream, ct).ConfigureAwait(false);
        } finally {
            tracker.AddNetwork(Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>下载 artifact — 自动计网络耗时</summary>
    public async Task<GitHubApiResponse> DownloadArtifactAsync(
        string owner, string repo, long artifactId, string filePath, CancellationToken ct = default) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        if (tracker is null) return await _inner.DownloadArtifactAsync(owner, repo, artifactId, filePath, ct).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        try {
            return await _inner.DownloadArtifactAsync(owner, repo, artifactId, filePath, ct).ConfigureAwait(false);
        } finally {
            tracker.AddNetwork(Stopwatch.GetTimestamp() - start);
        }
    }
}
