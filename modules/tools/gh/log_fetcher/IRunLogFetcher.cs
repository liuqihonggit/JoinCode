namespace McpToolDispatch;

/// <summary>
/// Run 日志下载策略 — 模块化日志获取方式,优先并行下载
/// <para>实现: ParallelJobLogFetcher(默认,并行逐job纯文本) / ZipRunLogFetcher(备选,整run zip)</para>
/// <para>切换: JCC_RUN_LOG_FETCHER=zip 用 zip 策略,默认并行</para>
/// </summary>
internal interface IRunLogFetcher {
    /// <summary>获取整个 Run 的日志流(逐行 yield)</summary>
    /// <param name="owner">仓库 owner</param>
    /// <param name="repo">仓库名</param>
    /// <param name="runId">Run ID</param>
    /// <param name="wantRefresh">true 跳过缓存读(仍写缓存),用于 rerun 后避免脏数据</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="maxConcurrency">并行度,0=用默认 GetMaxConcurrency()</param>
    IAsyncEnumerable<string> FetchAsync(string owner, string repo, string runId, bool wantRefresh, CancellationToken ct, int maxConcurrency = 0);
}
