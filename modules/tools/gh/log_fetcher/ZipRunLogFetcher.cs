namespace McpToolDispatch;

/// <summary>
/// 整 Run zip 下载 — 备选策略,包含 run 级元数据
/// <para>下载整 run 日志(zip 格式),全量缓冲到 MemoryStream(ZipArchive 需要 seek)</para>
/// <para>适合需要 run 级元数据的场景;大 run 有 OOM 风险</para>
/// </summary>
internal sealed class ZipRunLogFetcher(GitHubRunLogFilterRunner runner) : IRunLogFetcher {
    /// <summary>下载整 Run zip 日志(含 run 级元数据),全量缓冲到 MemoryStream</summary>
    public IAsyncEnumerable<string> FetchAsync(string owner, string repo, string runId, bool wantRefresh, CancellationToken ct, int maxConcurrency = 0) {
        return runner.GetOrFetchRunLogsAsync(owner, repo, runId, wantRefresh, ct);
    }
}
