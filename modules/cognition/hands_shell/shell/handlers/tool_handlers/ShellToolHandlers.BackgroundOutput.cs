namespace Tools.Handlers;

/// <summary>
/// shell_background_output 的读取语义 — 对齐基准 TaskOutput 的 block/timeout 契约：
/// 默认阻塞到任务结束（上限 30s），超时返回已产出的部分输出 + retrieval_status，
/// 且超时只结束本次等待，不取消后台任务。
/// </summary>
public partial class ShellToolHandlers {
    /// <summary>默认阻塞等待上限（毫秒）— 对齐基准 30000。</summary>
    internal const int DefaultOutputTimeoutMs = 30_000;

    /// <summary>等待上限硬上限（毫秒）— 对齐基准 600000。</summary>
    internal const int MaxOutputTimeoutMs = 600_000;

    /// <summary>取数状态 — 与基准 retrieval_status 同名同义。</summary>
    internal enum ShellOutputRetrieval {
        /// <summary>任务已到终态，输出完整。</summary>
        Success,
        /// <summary>block=false 且任务仍在跑，输出为当前快照。</summary>
        NotReady,
        /// <summary>block=true 但等待超时，输出为当前快照，任务未取消。</summary>
        Timeout,
    }

    /// <summary>解析后的等待参数（已夹取范围）。</summary>
    internal readonly record struct BackgroundOutputWaitOptions(bool Block, int TimeoutMs);

    /// <summary>解析等待参数 — 缺省对齐基准：block=true、timeout=30000ms。</summary>
    internal static BackgroundOutputWaitOptions ResolveWaitOptions(bool? block, int? timeoutMs) =>
        new(
            Block: block ?? true,
            TimeoutMs: Math.Clamp(timeoutMs ?? DefaultOutputTimeoutMs, 0, MaxOutputTimeoutMs));

    /// <summary>
    /// 读取后台任务输出 — 唯一实现，供工具方法与测试共用。
    /// </summary>
    internal static async Task<ToolResult> ReadBackgroundOutputAsync(
        ISystemActuatorRegistry registry,
        string taskId,
        bool? block,
        int? timeoutMs,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(taskId)) {
            var emptyDiag = BuildEmptyTaskIdDiagnostic();
            return ToolResultBuilder.Error().WithText(emptyDiag.FormattedMessage).WithDiagnostic(emptyDiag).Build();
        }

        var task = await registry.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null) {
            var notFoundDiag = BuildTaskNotFoundDiagnostic(taskId);
            return ToolResultBuilder.Error().WithText(notFoundDiag.FormattedMessage).WithDiagnostic(notFoundDiag).Build();
        }

        var options = ResolveWaitOptions(block, timeoutMs);
        if (options.Block) {
            await WaitForCompletionAsync(registry, taskId, options.TimeoutMs, cancellationToken).ConfigureAwait(false);
        }

        var latest = await registry.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false) ?? task;
        var output = await registry.GetTaskOutputAsync(taskId, cancellationToken).ConfigureAwait(false);
        var retrieval = ResolveRetrieval(latest.Status, options.Block);

        return ToolResultBuilder.Success().WithText(FormatBackgroundOutput(latest, retrieval, output)).Build();
    }

    /// <summary>
    /// 阻塞等待任务到终态 — 超时只取消本次等待，不取消任务（任务仍归后台注册表所有）。
    /// </summary>
    internal static async Task WaitForCompletionAsync(
        ISystemActuatorRegistry registry,
        string taskId,
        int timeoutMs,
        CancellationToken cancellationToken) {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);
        try {
            await registry.WaitForTaskAsync(taskId, timeoutCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // 等待窗口用尽：任务照常在后台跑，交给调用方决定是否再次获取
        }
    }

    /// <summary>按任务状态与调用意图判定取数状态。</summary>
    internal static ShellOutputRetrieval ResolveRetrieval(TaskExecutionStatus status, bool block) =>
        BackgroundTaskStateTransitions.IsTerminal(status)
            ? ShellOutputRetrieval.Success
            : block ? ShellOutputRetrieval.Timeout : ShellOutputRetrieval.NotReady;

    /// <summary>格式化应答 — 状态标签在前，模型可直接判断该继续等还是读结果。</summary>
    internal static string FormatBackgroundOutput(
        SystemActuatorBackgroundTaskInfo task,
        ShellOutputRetrieval retrieval,
        string? output) {
        var response = new StringBuilder();
        response.AppendLine($"<retrieval_status>{ToRetrievalValue(retrieval)}</retrieval_status>");
        response.AppendLine($"<task_id>{task.TaskId}</task_id>");
        response.AppendLine($"<status>{ToStatusValue(task.Status)}</status>");
        if (task.ExitCode.HasValue)
            response.AppendLine($"<exit_code>{task.ExitCode.Value}</exit_code>");
        if (!string.IsNullOrEmpty(task.ErrorMessage))
            response.AppendLine($"<error>{task.ErrorMessage}</error>");

        response.AppendLine();
        if (string.IsNullOrWhiteSpace(output)) {
            response.AppendLine("<output>(no output yet)</output>");
        } else {
            response.AppendLine("<output>");
            response.AppendLine(output.TrimEnd());
            response.AppendLine("</output>");
        }

        var hint = BuildRetrievalHint(retrieval);
        if (hint.Length > 0) {
            response.AppendLine();
            response.Append(hint);
        }

        return response.ToString().TrimEnd();
    }

    /// <summary>非终态时给出下一步动作，避免模型把“还没跑完”误读成“工具坏了”。</summary>
    private static string BuildRetrievalHint(ShellOutputRetrieval retrieval) => retrieval switch {
        ShellOutputRetrieval.Timeout =>
            $"任务仍在后台运行（未取消），以上是当前输出。继续等待就再次调用（默认再等 {DefaultOutputTimeoutMs}ms）；只要当前进度请传 block=false。",
        ShellOutputRetrieval.NotReady =>
            "任务仍在运行，以上是当前输出。要等它结束请传 block=true（默认），或稍后再次获取。",
        _ => string.Empty,
    };

    private static string ToRetrievalValue(ShellOutputRetrieval retrieval) => retrieval switch {
        ShellOutputRetrieval.Success => "success",
        ShellOutputRetrieval.NotReady => "not_ready",
        ShellOutputRetrieval.Timeout => "timeout",
        _ => "timeout",
    };

    private static string ToStatusValue(TaskExecutionStatus status) => status switch {
        TaskExecutionStatus.Pending => "pending",
        TaskExecutionStatus.Running => "running",
        TaskExecutionStatus.Paused => "paused",
        TaskExecutionStatus.Completed => "completed",
        TaskExecutionStatus.Failed => "failed",
        TaskExecutionStatus.Cancelled => "cancelled",
        TaskExecutionStatus.WaitingForDependency => "waiting_for_dependency",
        _ => status.ToString().ToLowerInvariant(),
    };
}
