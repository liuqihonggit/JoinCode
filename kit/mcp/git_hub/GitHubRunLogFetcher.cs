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
            using var doc = JsonDocument.Parse(jobsResult.Body);
            if (!doc.RootElement.TryGetProperty("jobs", out var jobsEl))
                return GitHubToolHandlers.Fail("未找到 jobs 数据");

            foreach (var job in jobsEl.EnumerateArray()) {
                var id = job.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64() : 0;
                var name = job.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "unknown" : "unknown";
                var status = job.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "?" : "?";
                var conclusion = job.TryGetProperty("conclusion", out var conEl) ? conEl.GetString() ?? "" : "";
                jobs.Add((id, name, status, conclusion));
            }
        } catch (Exception ex) {
            return GitHubToolHandlers.Fail($"解析 job 列表失败: {ex.Message}");
        }

        // 优化A2: 汇总前置+全量失败/cancelled/in_progress+success 折叠到5(AI 首屏定位问题降 token)
        var failedCount = jobs.Count(j => j.conclusion == "failure");
        var cancelledCount = jobs.Count(j => j.conclusion == "cancelled");
        var inProgressCount = jobs.Count(j => j.status == "in_progress");
        var successCount = jobs.Count(j => j.conclusion == "success");
        var totalCount = jobs.Count;

        var nonSuccess = jobs
            .Where(j => j.conclusion != "success")
            .OrderBy(j => JobSortKey(j.conclusion, j.status))
            .ThenBy(j => j.name)
            .ToList();
        var successJobs = jobs
            .Where(j => j.conclusion == "success")
            .OrderBy(j => j.name)
            .ToList();
        const int maxSuccessDisplay = 5;
        var displayedSuccess = successJobs.Take(maxSuccessDisplay).ToList();
        var omittedSuccess = successCount - displayedSuccess.Count;

        var sb = new StringBuilder();
        sb.Append($"汇总: {totalCount} 个 job, {failedCount} 个失败, {cancelledCount} 个取消, {inProgressCount} 个进行中, {successCount} 个成功");
        foreach (var (id, name, status, conclusion) in nonSuccess) {
            var marker = conclusion switch {
                "failure" => "❌",
                "cancelled" => "⊘",
                _ when status == "in_progress" => "⏳",
                _ => "  "
            };
            sb.Append($"\n  {marker} {id,15}  {name}  [{conclusion}]");
        }
        foreach (var (id, name, status, conclusion) in displayedSuccess)
            sb.Append($"\n  ✅ {id,15}  {name}  [success]");
        if (omittedSuccess > 0)
            sb.Append($"\n  … 另有 {omittedSuccess} 个 success job 未列出");

        var hint = failedCount > 0
            ? $"\n\n💡 下一步:\n- expand=failed → 直接拉失败步骤日志(量少)\n- expand=steps job_id=<失败job的ID> → 下载指定 job 日志并查看步骤列表\n- 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456"
            : "\n\n💡 下一步:\n- expand=steps job_id=<job ID> → 下载指定 job 日志并查看步骤列表\n- 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456";

        return GitHubToolHandlers.Ok(sb.ToString() + hint, $"Run {runId} job 列表({totalCount} 个,{failedCount} 个失败):");
    }

    /// <summary>job 排序键: failure=0, cancelled=1, in_progress=2, success=3, 其他=4</summary>
    private static int JobSortKey(string conclusion, string status)
        => (conclusion, status) switch {
            ("failure", _) => 0,
            ("cancelled", _) => 1,
            (_, "in_progress") => 2,
            ("success", _) => 3,
            _ => 4,
        };
}
