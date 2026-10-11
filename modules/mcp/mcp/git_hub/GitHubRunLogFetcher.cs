namespace McpToolDispatch;

/// <summary>
/// GitHub Run 日志获取器 — 接收 IGitHubApiClient 参数,负责 job 列表/日志下载/缓存
/// </summary>
internal sealed class GitHubRunLogFetcher {
    /// <summary>
    /// 列出 Run 的 job 列表(轻量,不下载日志)
    /// <para>返回 job ID/名称/状态/结论,AI 选择目标 job 后用 expand=steps job_id=xxx 按需下载</para>
    /// </summary>
    public async Task<ToolResult> ListJobsAsync(IGitHubApiClient client, string owner, string repo, string runId, CancellationToken ct) {
        var jobsResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs", paginate: true, ct: ct).ConfigureAwait(false);
        if (!jobsResult.Success) return GitHubToolHandlers.Fail(jobsResult.Error);

        var jobs = new List<(long id, string name, string status, string conclusion)>();
        try {
            var jobsResp = JsonSerializer.Deserialize(jobsResult.Body, GitHubApiJsonContext.Safe.RunJobListResponse);
            if (jobsResp?.Jobs is null)
                return GitHubToolHandlers.Fail("未找到 jobs 数据");

            foreach (var job in jobsResp.Jobs) {
                jobs.Add((job.Id, job.Name, job.Status, job.Conclusion ?? ""));
            }
        } catch (Exception ex) {
            return GitHubToolHandlers.Fail($"解析 job 列表失败: {ex.Message}");
        }

        // 状态机: 异常(failure+timed_out+cancelled)完整展开,通过默认折叠 — AI 首屏定位问题降 token
        var jobSeverities = jobs.Select(j => (j.id, j.name, j.conclusion, severity: CiJobSeverityExtensions.FromConclusion(j.conclusion, j.status))).ToList();
        var anomalyCount = jobSeverities.Count(j => j.severity.IsAnomaly());
        var pendingCount = jobSeverities.Count(j => j.severity == CiJobSeverity.Pending);
        var successCount = jobSeverities.Count(j => j.severity == CiJobSeverity.Success);
        var skippedCount = jobSeverities.Count(j => j.severity == CiJobSeverity.Skipped);
        var totalCount = jobs.Count;

        var nonSuccess = jobSeverities
            .Where(j => j.severity != CiJobSeverity.Success)
            .OrderBy(j => j.severity.SortKey())
            .ThenBy(j => j.name)
            .ToList();
        var successJobs = jobSeverities
            .Where(j => j.severity == CiJobSeverity.Success)
            .OrderBy(j => j.name)
            .ToList();
        const int maxSuccessDisplay = 5;
        var displayedSuccess = successJobs.Take(maxSuccessDisplay).ToList();
        var omittedSuccess = successCount - displayedSuccess.Count;

        // 缺陷5: 限制非成功 job 显示数量,避免超 2000 字符触发 McpResultCollapseClassifier 折叠
        const int maxNonSuccessDisplay = 25;
        var displayedNonSuccess = nonSuccess.Take(maxNonSuccessDisplay).ToList();
        var omittedNonSuccess = nonSuccess.Count - displayedNonSuccess.Count;

        var sb = new StringBuilder();
        sb.Append($"汇总: {totalCount} 个 job, {anomalyCount} 个异常, {pendingCount} 个进行中, {successCount} 个成功, {skippedCount} 个跳过(依赖链跳过,非失败)");
        foreach (var (id, name, conclusion, severity) in displayedNonSuccess) {
            var marker = severity.Marker();
            sb.Append($"\n  {marker} {id,15}  {name}  [{conclusion}]");
        }
        if (omittedNonSuccess > 0)
            sb.Append($"\n  … 另有 {omittedNonSuccess} 个非成功 job 未列出(用 --json_fields id,name,conclusion 查看完整列表)");
        foreach (var (id, name, conclusion, severity) in displayedSuccess)
            sb.Append($"\n  ✅ {id,15}  {name}  [success]");
        if (omittedSuccess > 0)
            sb.Append($"\n  … 另有 {omittedSuccess} 个 success job 未列出");

        var hint = anomalyCount > 0
            ? $"\n\n💡 下一步:\n- expand=failed → 直接拉异常步骤日志(含 failure+timed_out+cancelled,量少)\n- expand=steps job_id=<异常job的ID> → 下载指定 job 日志并查看步骤列表\n- 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456"
            : "\n\n💡 下一步:\n- expand=steps job_id=<job ID> → 下载指定 job 日志并查看步骤列表\n- 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456";

        return GitHubToolHandlers.Ok(sb.ToString() + hint, $"Run {runId} job 列表({totalCount} 个,{anomalyCount} 个异常):");
    }
}
