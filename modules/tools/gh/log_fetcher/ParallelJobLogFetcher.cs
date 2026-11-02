namespace McpToolDispatch;

/// <summary>
/// 并行逐 job 下载纯文本日志 — 默认策略,最快
/// <para>优先下载异常 job(失败/取消),AI 二次下载其余;避免全量下载 26+ job 耗时</para>
/// <para>每 job 走纯文本 API(可流式 break),多 job 并行下载(Channel+SemaphoreSlim)</para>
/// <para>每 job 独立 LSM 缓存(key=gh:log:{jobId}),粒度更细,单 job 命中直接 yield</para>
/// </summary>
internal sealed class ParallelJobLogFetcher(GitHubRunLogFilterRunner runner) : IRunLogFetcher {
    /// <summary>优先并行下载异常 job 日志,提示 AI 二次下载其余成功 job</summary>
    public async IAsyncEnumerable<string> FetchAsync(string owner, string repo, string runId, bool wantRefresh,
        [EnumeratorCancellation] CancellationToken ct, int maxConcurrency = 0) {
        if (maxConcurrency <= 0) maxConcurrency = GitHubRunLogFilterRunner.GetMaxConcurrency();
        var jobs = await runner.GetOrFetchJobsAsync(owner, repo, runId, wantRefresh, ct).ConfigureAwait(false);

        // job 列表为空: 回退到整 run 下载(API 异常或空 run 场景)
        if (jobs.Count == 0) {
            await foreach (var line in runner.GetOrFetchRunLogsAsync(owner, repo, runId, wantRefresh, ct).ConfigureAwait(false)) {
                yield return line;
            }
            yield break;
        }

        // 分类: 异常 job(优先下载) + 正常 job(提示 AI 二次下载)
        var anomalyJobIds = new List<long>(jobs.Count);
        var successCount = 0;
        foreach (var j in jobs) {
            if (j.Id == 0) continue;
            var severity = CiJobSeverityExtensions.FromConclusion(j.Conclusion, j.Status);
            if (severity.IsAnomaly())
                anomalyJobIds.Add(j.Id);
            else
                successCount++;
        }

        // 无异常 job: 提示全部成功
        if (anomalyJobIds.Count == 0) {
            yield return $"所有 {jobs.Count} 个 job 均成功,无错误日志。用 --job_id <id> 查看指定 job,或 JCC_RUN_LOG_FETCHER=zip 下载整 run";
            yield break;
        }

        // 优先并行下载异常 job
        await foreach (var line in runner.DownloadJobsParallelAsync(owner, repo, runId, anomalyJobIds, wantRefresh, ct, maxConcurrency).ConfigureAwait(false)) {
            yield return line;
        }

        // 提示: 另有成功 job 可二次下载
        if (successCount > 0) {
            yield return $"\n💡 另有 {successCount} 个成功 job 未下载。用 --job_id <id> 查看指定 job,或 JCC_RUN_LOG_FETCHER=zip 下载全部";
        }
    }
}
