// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace McpToolDispatch;

/// <summary>
/// GitHub Actions 轮询器 — 状态机驱动指数退避轮询,完成才返回唤醒 LLM
/// <para>替代 LLM 的 sleep+轮询模式:工具内部阻塞等待,一次 LLM 往返拿到最终结果</para>
/// <para>状态机: Polling → Completed/Timeout/Error(终态),Transition 为纯函数</para>
/// <para>指数退避: 初始间隔 ×1.5 每次,上限 60s</para>
/// <para>公共引擎 PollUntilAsync 消除两个 wait 方法的重复,各方法只传 poll + progressMessage</para>
/// </summary>
internal static class GitHubRunPoller {
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(60);

    // === 公共轮询引擎(状态机驱动) ===

    /// <summary>
    /// 状态机轮询引擎 — 单一循环编排,状态转换/退避/进度报告均为纯函数
    /// <para>循环体: poll → transition → (终态返回 / 进度报告 → 退避延迟)</para>
    /// </summary>
    /// <typeparam name="T">轮询值类型(RunPollData/PrPollData)</typeparam>
    /// <param name="pollOnce">单次轮询 IO 操作,返回 (Kind, Value, Error)</param>
    /// <param name="progressMessage">从轮询值提取进度核心消息(不含轮询次数,引擎附加)</param>
    /// <param name="timeout">总超时</param>
    /// <param name="initialInterval">初始轮询间隔</param>
    /// <param name="onProgress">进度回调(可为空)</param>
    /// <param name="progressType">进度类型标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>(State, Value, Error, PollCount, ElapsedMs)</returns>
    private static async Task<(PollState State, T Value, string? Error, int PollCount, long ElapsedMs)> PollUntilAsync<T>(
        Func<CancellationToken, Task<(PollOutcomeKind Kind, T Value, string? Error)>> pollOnce,
        Func<T, string> progressMessage,
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

            var (kind, value, error) = await pollOnce(ct).ConfigureAwait(false);
            var elapsedMs = Environment.TickCount64 - startTime;
            var state = Transition(kind, deadline);

            if (state is not PollState.Polling)
                return (state, value, error, pollCount, elapsedMs);

            ReportProgress(onProgress, progressType, progressMessage(value), pollCount, elapsedMs);
            await Task.Delay(interval, ct).ConfigureAwait(false);
            interval = ExponentialBackoff(interval);
        }
    }

    /// <summary>状态转换 — 纯函数,无 IO</summary>
    private static PollState Transition(PollOutcomeKind kind, DateTimeOffset deadline) => kind switch {
        PollOutcomeKind.Error => PollState.Error,
        PollOutcomeKind.Completed => PollState.Completed,
        PollOutcomeKind.FailFast => PollState.FastFailed,
        _ => DateTimeOffset.UtcNow >= deadline ? PollState.Timeout : PollState.Polling
    };

    /// <summary>指数退避 — 纯函数</summary>
    private static TimeSpan ExponentialBackoff(TimeSpan interval)
        => TimeSpan.FromTicks(Math.Min(interval.Ticks * 3 / 2, MaxPollInterval.Ticks));

    private static void ReportProgress(
        ToolProgressCallback? onProgress, string progressType,
        string coreMessage, int pollCount, long elapsedMs) {
        if (onProgress is null) return;
        onProgress(new ToolProgressData {
            ProgressType = progressType,
            ToolUseId = $"{progressType}-{pollCount}",
            Message = $"{coreMessage} (第 {pollCount} 次轮询, 已等待 {elapsedMs / 1000}s)",
            ElapsedTimeMs = elapsedMs,
        });
    }

    // === Run 等待 ===

    /// <summary>
    /// 轮询 Actions Run 直到 status==completed、超时或取消
    /// </summary>
    internal static async Task<RunWaitResult> WaitForRunCompletionAsync(
        IGitHubApiClient apiClient, string owner, string repo, string runId,
        TimeSpan timeout, TimeSpan initialInterval,
        ToolProgressCallback? onProgress, string progressType,
        CancellationToken ct) {
        var (state, value, error, pollCount, elapsedMs) = await PollUntilAsync(
            ct => PollRunOnceAsync(apiClient, owner, repo, runId, ct),
            v => $"Run {runId}: {v.Status}",
            timeout, initialInterval, onProgress, progressType, ct).ConfigureAwait(false);

        return state switch {
            PollState.Completed => RunWaitResult.Completed(value.Conclusion ?? "unknown", value.Body, pollCount, elapsedMs),
            PollState.Timeout => RunWaitResult.FromTimeout(value.Status, value.Body, pollCount, elapsedMs),
            _ => RunWaitResult.FromError(error ?? "轮询失败", pollCount)
        };
    }

    /// <summary>
    /// 单次 Run 轮询 — IO 操作,返回 (Kind, Value, Error)
    /// </summary>
    private static async Task<(PollOutcomeKind, RunPollData, string?)> PollRunOnceAsync(
        IGitHubApiClient apiClient, string owner, string repo, string runId, CancellationToken ct) {
        var result = await apiClient.SendAsync(
            HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}", ct: ct).ConfigureAwait(false);
        if (!result.Success)
            return (PollOutcomeKind.Error, default!, result.Error);

        var (status, conclusion) = ParseRunStatus(result.Body);
        var data = new RunPollData { Status = status ?? "unknown", Conclusion = conclusion, Body = result.Body };
        var kind = status == "completed" ? PollOutcomeKind.Completed : PollOutcomeKind.Continue;
        return (kind, data, null);
    }

    // === PR checks 等待 ===

    /// <summary>
    /// 轮询 PR 所有 check-runs 直到全部 status==completed、超时或取消
    /// </summary>
    internal static async Task<PrWaitResult> WaitForPrChecksCompletionAsync(
        IGitHubApiClient apiClient, string owner, string repo, string headSha, string prNumber,
        TimeSpan timeout, TimeSpan initialInterval,
        ToolProgressCallback? onProgress, string progressType,
        CancellationToken ct) {
        var (state, value, error, pollCount, elapsedMs) = await PollUntilAsync(
            ct => PollPrChecksOnceAsync(apiClient, owner, repo, headSha, ct),
            v => $"PR #{prNumber}: {v.Summary}",
            timeout, initialInterval, onProgress, progressType, ct).ConfigureAwait(false);

        return state switch {
            PollState.Completed => PrWaitResult.Completed(value.Summary, value.FailCount, value.Body, pollCount, elapsedMs),
            PollState.Timeout => PrWaitResult.FromTimeout(value.Summary, value.Body, pollCount, elapsedMs),
            _ => PrWaitResult.FromError(error ?? "轮询失败", pollCount)
        };
    }

    /// <summary>
    /// 单次 PR checks 轮询 — IO 操作,返回 (Kind, Value, Error)
    /// </summary>
    private static async Task<(PollOutcomeKind, PrPollData, string?)> PollPrChecksOnceAsync(
        IGitHubApiClient apiClient, string owner, string repo, string headSha, CancellationToken ct) {
        var result = await apiClient.SendAsync(
            HttpMethod.Get, $"repos/{owner}/{repo}/commits/{headSha}/check-runs",
            query: new Dictionary<string, string> { ["per_page"] = "100" }, ct: ct).ConfigureAwait(false);
        if (!result.Success)
            return (PollOutcomeKind.Error, default!, result.Error);

        var (allCompleted, summary, failCount) = ParseCheckRunsStatus(result.Body);
        var data = new PrPollData { Summary = summary, FailCount = failCount, Body = result.Body };
        var kind = allCompleted ? PollOutcomeKind.Completed : PollOutcomeKind.Continue;
        return (kind, data, null);
    }

    // === Run jobs fail-fast 监控 ===

    /// <summary>
    /// 轮询 Run 的 jobs,发现任何 job failure 立即返回(fail-fast),或 run completed 返回
    /// <para>并行查 run status + jobs,发现 conclusion=failure/cancelled/timed_out 的 job 立即终止</para>
    /// <para>替代等整个 run completed 才发现错误:CI 运行中某 job 失败即可拉日志唤醒 LLM</para>
    /// </summary>
    internal static async Task<JobWatchResult> WatchRunJobsForFailureAsync(
        IGitHubApiClient apiClient, string owner, string repo, string runId,
        TimeSpan timeout, TimeSpan initialInterval,
        ToolProgressCallback? onProgress, string progressType,
        CancellationToken ct) {
        var (state, value, error, pollCount, elapsedMs) = await PollUntilAsync(
            ct => PollRunJobsOnceAsync(apiClient, owner, repo, runId, ct),
            v => $"Run {runId}: {v.Summary}",
            timeout, initialInterval, onProgress, progressType, ct).ConfigureAwait(false);

        return state switch {
            PollState.Completed => JobWatchResult.Completed(value.RunConclusion, value.FailedJobIds, value.Summary, pollCount, elapsedMs),
            PollState.FastFailed => JobWatchResult.FailFast(value.FailedJobIds, value.Summary, pollCount, elapsedMs),
            PollState.Timeout => JobWatchResult.FromTimeout(value.Summary, pollCount, elapsedMs),
            _ => JobWatchResult.FromError(error ?? "轮询失败", pollCount)
        };
    }

    /// <summary>
    /// 单次 Run jobs 轮询 — 并行查 run status + jobs,发现 failure job 返回 FailFast
    /// </summary>
    private static async Task<(PollOutcomeKind, JobWatchData, string?)> PollRunJobsOnceAsync(
        IGitHubApiClient apiClient, string owner, string repo, string runId, CancellationToken ct) {
        var runTask = apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}", ct: ct);
        var jobsTask = apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs",
            query: new Dictionary<string, string> { ["per_page"] = "100" }, paginate: true, ct: ct);
        await Task.WhenAll(runTask, jobsTask).ConfigureAwait(false);
        var runResult = await runTask.ConfigureAwait(false);
        var jobsResult = await jobsTask.ConfigureAwait(false);

        if (!runResult.Success) return (PollOutcomeKind.Error, default!, runResult.Error);
        if (!jobsResult.Success) return (PollOutcomeKind.Error, default!, jobsResult.Error);

        var (runStatus, runConclusion) = ParseRunStatus(runResult.Body);
        var (failedJobIds, jobSummary) = ParseJobsForFailure(jobsResult.Body);

        var status = runStatus ?? "unknown";
        var summary = $"{jobSummary} | run: {status}";
        var data = new JobWatchData { RunStatus = status, RunConclusion = runConclusion, FailedJobIds = failedJobIds, Summary = summary };

        if (failedJobIds.Count > 0) return (PollOutcomeKind.FailFast, data, null);
        if (status == "completed") return (PollOutcomeKind.Completed, data, null);
        return (PollOutcomeKind.Continue, data, null);
    }

    /// <summary>
    /// 从 jobs JSON 解析失败 job ID 列表 + 进度汇总(纯函数)
    /// </summary>
    private static (List<long> failedJobIds, string summary) ParseJobsForFailure(string json) {
        var failedJobIds = new List<long>();
        var completed = 0; var inProgress = 0; var queued = 0; var failed = 0;
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.RunJobListResponse);
            if (resp?.Jobs is null) return ([], "解析 jobs 失败");
            foreach (var job in resp.Jobs) {
                var status = job.Status ?? "";
                var conclusion = job.Conclusion ?? "";
                if (status != "completed") {
                    if (status == "queued") queued++; else inProgress++;
                    continue;
                }
                if (conclusion is "failure" or "cancelled" or "timed_out") {
                    failed++;
                    if (job.Id != 0) failedJobIds.Add(job.Id);
                } else {
                    completed++;
                }
            }
        } catch {
            return ([], "解析 jobs 失败");
        }
        return (failedJobIds, $"{completed} 完成, {failed} 失败, {inProgress} 进行中, {queued} 排队");
    }

    // === JSON 解析(纯函数) ===

    /// <summary>
    /// 从 run JSON 解析 status 和 conclusion
    /// </summary>
    private static (string? status, string? conclusion) ParseRunStatus(string json) {
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.RunDetailResponse);
            return (resp?.Status, resp?.Conclusion);
        } catch {
            return (null, null);
        }
    }

    /// <summary>
    /// 从 check-runs JSON 解析是否全部完成,并构建汇总文本和失败数
    /// </summary>
    private static (bool allCompleted, string summary, int failCount) ParseCheckRunsStatus(string json) {
        var passCount = 0; var failCount = 0; var pendingCount = 0; var skipCount = 0;
        var allCompleted = true;
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.CheckRunListResponse);
            if (resp?.CheckRuns is null) return (false, "解析 check-runs 失败", 0);
            foreach (var run in resp.CheckRuns) {
                var status = run.Status ?? "";
                var conclusion = run.Conclusion;

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
            return (false, "解析 check-runs 失败", 0);
        }
        var summary = $"{passCount} 通过, {failCount} 失败, {pendingCount} 进行中, {skipCount} 跳过";
        return (allCompleted, summary, failCount);
    }
}

// === 状态机类型 ===

/// <summary>轮询结果类型(单次轮询 outcome)</summary>
internal enum PollOutcomeKind {
    /// <summary>已完成,触发返回</summary>
    Completed,
    /// <summary>未完成,继续轮询</summary>
    Continue,
    /// <summary>API 错误</summary>
    Error,
    /// <summary>fail-fast:发现失败,立即返回(不等 completed)</summary>
    FailFast
}

/// <summary>状态机状态</summary>
internal enum PollState {
    /// <summary>轮询中</summary>
    Polling,
    /// <summary>已完成(终态)</summary>
    Completed,
    /// <summary>等待超时(终态)</summary>
    Timeout,
    /// <summary>API 错误(终态)</summary>
    Error,
    /// <summary>fail-fast 终态:发现失败 job,立即返回</summary>
    FastFailed
}

// === 轮询值类型 ===

/// <summary>Run 单次轮询值</summary>
internal sealed record RunPollData {
    /// <summary>Run status(queued/in_progress/completed)</summary>
    public required string Status { get; init; }
    /// <summary>Run conclusion(success/failure 等,completed 时有值)</summary>
    public string? Conclusion { get; init; }
    /// <summary>Run 详情 JSON 原文</summary>
    public required string Body { get; init; }
}

/// <summary>PR checks 单次轮询值</summary>
internal sealed record PrPollData {
    /// <summary>checks 汇总文本(N 通过, M 失败, ...)</summary>
    public required string Summary { get; init; }
    /// <summary>失败的 check 数量</summary>
    public int FailCount { get; init; }
    /// <summary>check-runs JSON 原文</summary>
    public required string Body { get; init; }
}

/// <summary>Run jobs fail-fast 监控单次轮询值</summary>
internal sealed record JobWatchData {
    /// <summary>Run status(queued/in_progress/completed)</summary>
    public required string RunStatus { get; init; }
    /// <summary>Run conclusion(completed 时有值)</summary>
    public string? RunConclusion { get; init; }
    /// <summary>已失败的 job ID 列表(fail-fast 触发依据)</summary>
    public List<long> FailedJobIds { get; init; } = [];
    /// <summary>进度汇总文本(N 完成, M 失败, ...)</summary>
    public required string Summary { get; init; }
}

// === 等待结果 ===

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
    /// <summary>失败的 check 数量(Completed 时填充)</summary>
    public int FailCount { get; init; }
    /// <summary>check-runs JSON 原文</summary>
    public string? Body { get; init; }
    /// <summary>错误信息(Error 时填充)</summary>
    public string? Error { get; init; }
    /// <summary>轮询次数</summary>
    public int PollCount { get; init; }
    /// <summary>已耗时(毫秒)</summary>
    public long ElapsedMs { get; init; }

    internal static PrWaitResult Completed(string summary, int failCount, string body, int pollCount, long elapsedMs)
        => new() { Outcome = RunWaitOutcome.Completed, Summary = summary, FailCount = failCount, Body = body, PollCount = pollCount, ElapsedMs = elapsedMs };

    internal static PrWaitResult FromTimeout(string summary, string body, int pollCount, long elapsedMs)
        => new() { Outcome = RunWaitOutcome.Timeout, Summary = summary, Body = body, PollCount = pollCount, ElapsedMs = elapsedMs };

    internal static PrWaitResult FromError(string error, int pollCount)
        => new() { Outcome = RunWaitOutcome.Error, Error = error, PollCount = pollCount };
}

/// <summary>
/// Run jobs fail-fast 监控结果
/// </summary>
internal sealed record JobWatchResult {
    /// <summary>结果类型(Completed/FailFast/Timeout/Error)</summary>
    public required RunWaitOutcome Outcome { get; init; }
    /// <summary>Run conclusion(Completed 时填充)</summary>
    public string? RunConclusion { get; init; }
    /// <summary>失败 job ID 列表(FailFast/Completed 时填充)</summary>
    public List<long> FailedJobIds { get; init; } = [];
    /// <summary>进度汇总文本</summary>
    public string? Summary { get; init; }
    /// <summary>错误信息(Error 时填充)</summary>
    public string? Error { get; init; }
    /// <summary>轮询次数</summary>
    public int PollCount { get; init; }
    /// <summary>已耗时(毫秒)</summary>
    public long ElapsedMs { get; init; }

    internal static JobWatchResult Completed(string? conclusion, List<long> failedJobIds, string summary, int pollCount, long elapsedMs)
        => new() { Outcome = RunWaitOutcome.Completed, RunConclusion = conclusion, FailedJobIds = failedJobIds, Summary = summary, PollCount = pollCount, ElapsedMs = elapsedMs };

    internal static JobWatchResult FailFast(List<long> failedJobIds, string summary, int pollCount, long elapsedMs)
        => new() { Outcome = RunWaitOutcome.FailFast, FailedJobIds = failedJobIds, Summary = summary, PollCount = pollCount, ElapsedMs = elapsedMs };

    internal static JobWatchResult FromTimeout(string summary, int pollCount, long elapsedMs)
        => new() { Outcome = RunWaitOutcome.Timeout, Summary = summary, PollCount = pollCount, ElapsedMs = elapsedMs };

    internal static JobWatchResult FromError(string error, int pollCount)
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
    Error,
    /// <summary>fail-fast:运行中发现失败 job,立即返回(不等 run completed)</summary>
    FailFast
}
