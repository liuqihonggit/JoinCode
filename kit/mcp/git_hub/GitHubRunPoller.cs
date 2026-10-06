namespace McpToolDispatch;

/// <summary>
/// GitHub Actions 轮询器 — 指数退避轮询直到 CI 完成,通过 onProgress 报告进度,完成才返回唤醒 LLM
/// <para>替代 LLM 的 sleep+轮询模式:工具内部阻塞等待,一次 LLM 往返拿到最终结果</para>
/// <para>信号模型:轮询发现 status==completed 触发返回(唤醒 LLM),非 sleep 固定等待</para>
/// <para>指数退避:初始间隔 ×1.5 每次,上限 60s,平衡 API 速率限制与响应速度</para>
/// </summary>
internal static class GitHubRunPoller {
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 轮询 Actions Run 直到 status==completed、超时或取消
    /// </summary>
    /// <param name="apiClient">GitHub API 客户端</param>
    /// <param name="owner">仓库 owner</param>
    /// <param name="repo">仓库名</param>
    /// <param name="runId">Run ID</param>
    /// <param name="timeout">总超时</param>
    /// <param name="initialInterval">初始轮询间隔</param>
    /// <param name="onProgress">进度回调(可为空)</param>
    /// <param name="progressType">进度类型标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>轮询结果(Completed/Timeout/Error)</returns>
    internal static async Task<RunWaitResult> WaitForRunCompletionAsync(
        IGitHubApiClient apiClient, string owner, string repo, string runId,
        TimeSpan timeout, TimeSpan initialInterval,
        ToolProgressCallback? onProgress, string progressType,
        CancellationToken ct) {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var interval = initialInterval;
        var pollCount = 0;
        var startTime = Environment.TickCount64;

        while (true) {
            ct.ThrowIfCancellationRequested();
            pollCount++;

            var result = await apiClient.SendAsync(
                HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}", ct: ct).ConfigureAwait(false);
            if (!result.Success)
                return RunWaitResult.FromError(result.Error, pollCount);

            var (status, conclusion) = ParseRunStatus(result.Body);
            var elapsedMs = Environment.TickCount64 - startTime;

            if (status == "completed")
                return RunWaitResult.Completed(conclusion ?? "unknown", result.Body, pollCount, elapsedMs);

            if (DateTimeOffset.UtcNow >= deadline)
                return RunWaitResult.FromTimeout(status ?? "unknown", result.Body, pollCount, elapsedMs);

            ReportProgress(onProgress, progressType, runId, status ?? "unknown", pollCount, elapsedMs);
            await Task.Delay(interval, ct).ConfigureAwait(false);
            interval = TimeSpan.FromTicks(Math.Min(interval.Ticks * 3 / 2, MaxPollInterval.Ticks));
        }
    }

    /// <summary>
    /// 轮询 PR 所有 check-runs 直到全部 status==completed、超时或取消
    /// </summary>
    /// <param name="apiClient">GitHub API 客户端</param>
    /// <param name="owner">仓库 owner</param>
    /// <param name="repo">仓库名</param>
    /// <param name="headSha">PR head commit SHA</param>
    /// <param name="prNumber">PR 编号(用于进度报告)</param>
    /// <param name="timeout">总超时</param>
    /// <param name="initialInterval">初始轮询间隔</param>
    /// <param name="onProgress">进度回调(可为空)</param>
    /// <param name="progressType">进度类型标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>轮询结果(Completed/Timeout/Error + 各 check 汇总)</returns>
    internal static async Task<PrWaitResult> WaitForPrChecksCompletionAsync(
        IGitHubApiClient apiClient, string owner, string repo, string headSha, string prNumber,
        TimeSpan timeout, TimeSpan initialInterval,
        ToolProgressCallback? onProgress, string progressType,
        CancellationToken ct) {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var interval = initialInterval;
        var pollCount = 0;
        var startTime = Environment.TickCount64;

        while (true) {
            ct.ThrowIfCancellationRequested();
            pollCount++;

            var result = await apiClient.SendAsync(
                HttpMethod.Get, $"repos/{owner}/{repo}/commits/{headSha}/check-runs",
                query: new Dictionary<string, string> { ["per_page"] = "100" }, ct: ct).ConfigureAwait(false);
            if (!result.Success)
                return PrWaitResult.FromError(result.Error, pollCount);

            var (allCompleted, summary) = ParseCheckRunsStatus(result.Body);
            var elapsedMs = Environment.TickCount64 - startTime;

            if (allCompleted)
                return PrWaitResult.Completed(summary, result.Body, pollCount, elapsedMs);

            if (DateTimeOffset.UtcNow >= deadline)
                return PrWaitResult.FromTimeout(summary, result.Body, pollCount, elapsedMs);

            ReportPrProgress(onProgress, progressType, prNumber, summary, pollCount, elapsedMs);
            await Task.Delay(interval, ct).ConfigureAwait(false);
            interval = TimeSpan.FromTicks(Math.Min(interval.Ticks * 3 / 2, MaxPollInterval.Ticks));
        }
    }

    /// <summary>
    /// 从 run JSON 解析 status 和 conclusion
    /// </summary>
    private static (string? status, string? conclusion) ParseRunStatus(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
            var conclusion = root.TryGetProperty("conclusion", out var c) ? c.GetString() : null;
            return (status, conclusion);
        } catch {
            return (null, null);
        }
    }

    /// <summary>
    /// 从 check-runs JSON 解析是否全部完成,并构建汇总文本
    /// </summary>
    private static (bool allCompleted, string summary) ParseCheckRunsStatus(string json) {
        var passCount = 0; var failCount = 0; var pendingCount = 0; var skipCount = 0;
        var allCompleted = true;
        try {
            using var doc = JsonDocument.Parse(json);
            foreach (var run in doc.RootElement.GetProperty("check_runs").EnumerateArray()) {
                var status = run.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                var conclusion = run.TryGetProperty("conclusion", out var c) ? c.GetString() : null;

                if (status != "completed") {
                    allCompleted = false;
                    pendingCount++;
                    continue;
                }
                var display = conclusion switch {
                    "success" => "pass",
                    "failure" or "cancelled" or "timed_out" => "fail",
                    "skipped" or "neutral" => "skipping",
                    _ => "pending"
                };
                switch (display) {
                    case "pass": passCount++; break;
                    case "fail": failCount++; break;
                    case "skipping": skipCount++; break;
                    default: pendingCount++; break;
                }
            }
        } catch {
            return (false, "解析 check-runs 失败");
        }
        var summary = $"{passCount} 通过, {failCount} 失败, {pendingCount} 进行中, {skipCount} 跳过";
        return (allCompleted, summary);
    }

    private static void ReportProgress(
        ToolProgressCallback? onProgress, string progressType, string runId,
        string status, int pollCount, long elapsedMs) {
        if (onProgress is null) return;
        onProgress(new ToolProgressData {
            ProgressType = progressType,
            ToolUseId = $"{progressType}-{pollCount}",
            Message = $"Run {runId}: {status} (第 {pollCount} 次轮询, 已等待 {elapsedMs / 1000}s)",
            ElapsedTimeMs = elapsedMs,
        });
    }

    private static void ReportPrProgress(
        ToolProgressCallback? onProgress, string progressType, string prNumber,
        string summary, int pollCount, long elapsedMs) {
        if (onProgress is null) return;
        onProgress(new ToolProgressData {
            ProgressType = progressType,
            ToolUseId = $"{progressType}-{pollCount}",
            Message = $"PR #{prNumber}: {summary} (第 {pollCount} 次轮询, 已等待 {elapsedMs / 1000}s)",
            ElapsedTimeMs = elapsedMs,
        });
    }
}

/// <summary>
/// Run 等待结果
/// </summary>
internal sealed record RunWaitResult {
    /// <summary>结果类型(Completed/Timeout/Error)</summary>
    public required RunWaitOutcome Outcome { get; init; }
    /// <summary>Run conclusion(success/failure/cancelled 等),Timeout 时为当前 status</summary>
    public string? Conclusion { get; init; }
    /// <summary>Run 详情 JSON 原文</summary>
    public string? Body { get; init; }
    /// <summary>错误信息(Error 时填充)</summary>
    public string? Error { get; init; }
    /// <summary>轮询次数</summary>
    public int PollCount { get; init; }
    /// <summary>已耗时(毫秒)</summary>
    public long ElapsedMs { get; init; }

    internal static RunWaitResult Completed(string conclusion, string body, int pollCount, long elapsedMs)
        => new() { Outcome = RunWaitOutcome.Completed, Conclusion = conclusion, Body = body, PollCount = pollCount, ElapsedMs = elapsedMs };

    internal static RunWaitResult FromTimeout(string status, string body, int pollCount, long elapsedMs)
        => new() { Outcome = RunWaitOutcome.Timeout, Conclusion = status, Body = body, PollCount = pollCount, ElapsedMs = elapsedMs };

    internal static RunWaitResult FromError(string error, int pollCount)
        => new() { Outcome = RunWaitOutcome.Error, Error = error, PollCount = pollCount };
}

/// <summary>
/// PR checks 等待结果
/// </summary>
internal sealed record PrWaitResult {
    /// <summary>结果类型(Completed/Timeout/Error)</summary>
    public required RunWaitOutcome Outcome { get; init; }
    /// <summary>checks 汇总文本(N 通过, M 失败, ...)</summary>
    public string? Summary { get; init; }
    /// <summary>check-runs JSON 原文</summary>
    public string? Body { get; init; }
    /// <summary>错误信息(Error 时填充)</summary>
    public string? Error { get; init; }
    /// <summary>轮询次数</summary>
    public int PollCount { get; init; }
    /// <summary>已耗时(毫秒)</summary>
    public long ElapsedMs { get; init; }

    internal static PrWaitResult Completed(string summary, string body, int pollCount, long elapsedMs)
        => new() { Outcome = RunWaitOutcome.Completed, Summary = summary, Body = body, PollCount = pollCount, ElapsedMs = elapsedMs };

    internal static PrWaitResult FromTimeout(string summary, string body, int pollCount, long elapsedMs)
        => new() { Outcome = RunWaitOutcome.Timeout, Summary = summary, Body = body, PollCount = pollCount, ElapsedMs = elapsedMs };

    internal static PrWaitResult FromError(string error, int pollCount)
        => new() { Outcome = RunWaitOutcome.Error, Error = error, PollCount = pollCount };
}

/// <summary>
/// 等待结果类型
/// </summary>
internal enum RunWaitOutcome {
    /// <summary>已完成(status==completed)</summary>
    Completed,
    /// <summary>等待超时</summary>
    Timeout,
    /// <summary>API 调用错误</summary>
    Error
}
