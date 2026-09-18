namespace McpToolDispatch;

/// <summary>
/// GitHub Run 日志获取器 — 通过主类引用访问 _apiClient,负责 job 列表/日志下载/缓存
/// </summary>
internal sealed class GitHubRunLogFetcher
{
    private readonly GitHubToolHandlers _owner;

    /// <summary>
    /// 构造日志获取器,接受主类引用以访问 _apiClient 等内部成员
    /// </summary>
    public GitHubRunLogFetcher(GitHubToolHandlers owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// 列出 Run 的 job 列表(轻量,不下载日志)
    /// <para>返回 job ID/名称/状态/结论,AI 选择目标 job 后用 expand=steps job_id=xxx 按需下载</para>
    /// </summary>
    public async Task<ToolResult> ListJobsAsync(string owner, string repo, string runId, CancellationToken ct)
    {
        var jobsResult = await _owner._apiClient!.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs", paginate: true, ct: ct).ConfigureAwait(false);
        if (!jobsResult.Success) return GitHubToolHandlers.Fail(jobsResult.Error);

        var sb = new StringBuilder();
        var failedCount = 0;
        var totalCount = 0;
        try
        {
            using var doc = JsonDocument.Parse(jobsResult.Body);
            if (!doc.RootElement.TryGetProperty("jobs", out var jobsEl))
                return GitHubToolHandlers.Fail("未找到 jobs 数据");

            foreach (var job in jobsEl.EnumerateArray())
            {
                var id = job.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64() : 0;
                var name = job.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "unknown" : "unknown";
                var status = job.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "?" : "?";
                var conclusion = job.TryGetProperty("conclusion", out var conEl) ? conEl.GetString() ?? "" : "";
                totalCount++;

                var marker = conclusion switch
                {
                    "failure" => "❌",
                    "success" => "✅",
                    "cancelled" => "⊘",
                    _ when status == "in_progress" => "⏳",
                    _ => "  "
                };
                if (conclusion == "failure") failedCount++;

                sb.Append($"  {marker} {id,15}  {name}");
                if (!string.IsNullOrEmpty(conclusion))
                    sb.Append($"  [{conclusion}]");
                sb.Append('\n');
            }
        }
        catch (Exception ex)
        {
            return GitHubToolHandlers.Fail($"解析 job 列表失败: {ex.Message}");
        }

        var hint = failedCount > 0
            ? $"\n\n💡 下一步:\n- expand=failed → 直接拉失败步骤日志(量少)\n- expand=steps job_id=<失败job的ID> → 下载指定 job 日志并查看步骤列表\n- 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456"
            : "\n\n💡 下一步:\n- expand=steps job_id=<job ID> → 下载指定 job 日志并查看步骤列表\n- 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456";

        return GitHubToolHandlers.Ok(sb.ToString() + hint, $"Run {runId} job 列表({totalCount} 个,{failedCount} 个失败):");
    }
}
