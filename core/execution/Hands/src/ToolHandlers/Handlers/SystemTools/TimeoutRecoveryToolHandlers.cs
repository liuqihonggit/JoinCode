namespace Tools.Handlers;

/// <summary>
/// 超时续期工具处理器 — ToolKind.OnError，仅在工具执行超时失败时动态注入
/// 不出现在首次系统提示词中，仅在 OnErrorToolInjectionMiddleware 检测到超时错误后注入
/// 提供 resume/continue/stop 三个工具，形成纵深防御 Layer 2+3
/// </summary>
[McpToolDispatch(ToolCategory.ErrorRecovery, Kind = ToolKind.OnError)]
public class TimeoutRecoveryToolHandlers
{
    private readonly LongRunningTaskRegistry _taskRegistry;
    private readonly ILogger<TimeoutRecoveryToolHandlers>? _logger;

    public TimeoutRecoveryToolHandlers(LongRunningTaskRegistry taskRegistry, ILogger<TimeoutRecoveryToolHandlers>? logger = null)
    {
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _logger = logger;
    }

    /// <summary>
    /// 恢复超时任务 — 以10分钟（可配置）超时重新执行命令
    /// GroupName="Bash" 和 "PowerShell" 表示当 Bash/PowerShell 工具超时时精准推荐
    /// </summary>
    [McpTool("resume_timed_out_task", "恢复因超时被终止的命令，以更长超时重新执行", "error_recovery",
        Kind = JoinCode.Abstractions.Attributes.ToolKindConstants.OnError, GroupName = "Bash")]
    public async Task<ToolResult> ResumeTimedOutTaskAsync(
        [McpToolParameter("原始命令", Required = true)] string original_command,
        [McpToolParameter("原始工具名称 (Bash/PowerShell)", Required = true)] string original_tool,
        [McpToolParameter("续期超时分钟数 (默认10)", Required = false, DefaultValue = "10")] int? timeout_minutes = 10,
        [McpToolParameter("工作目录", Required = false)] string? working_directory = null,
        CancellationToken ct = default)
    {
        var timeoutMin = timeout_minutes is null or <= 0 ? 10 : timeout_minutes.Value;

        _logger?.LogInformation("恢复超时任务: tool={Tool}, timeout={Min}min, command={Cmd}", original_tool, timeoutMin, original_command);

        var result = await _taskRegistry.StartTaskAsync(original_command, original_tool, working_directory, timeoutMin, ct).ConfigureAwait(false);

        return BuildResult(result, original_command);
    }

    /// <summary>
    /// 继续等待运行中的任务 — 以10分钟（可配置）超时再次执行同一命令
    /// </summary>
    [McpTool("continue_long_running_task", "继续等待长时间运行的任务，以指定超时再次执行", "error_recovery",
        Kind = JoinCode.Abstractions.Attributes.ToolKindConstants.OnError, GroupName = "Bash")]
    public async Task<ToolResult> ContinueLongRunningTaskAsync(
        [McpToolParameter("任务ID", Required = true)] string task_id,
        [McpToolParameter("额外等待分钟数 (默认10)", Required = false, DefaultValue = "10")] int? additional_minutes = 10,
        CancellationToken ct = default)
    {
        var additionalMin = additional_minutes is null or <= 0 ? 10 : additional_minutes.Value;

        _logger?.LogInformation("继续长期任务: taskId={Id}, additional={Min}min", task_id, additionalMin);

        var result = await _taskRegistry.ContinueTaskAsync(task_id, additionalMin, ct).ConfigureAwait(false);

        var task = _taskRegistry.GetTask(task_id);
        return BuildResult(result, task?.Command ?? "(unknown)");
    }

    /// <summary>
    /// 终止运行中的任务
    /// </summary>
    [McpTool("stop_long_running_task", "终止长时间运行的任务", "error_recovery",
        Kind = JoinCode.Abstractions.Attributes.ToolKindConstants.OnError, GroupName = "Bash")]
    public Task<ToolResult> StopLongRunningTaskAsync(
        [McpToolParameter("任务ID", Required = true)] string task_id,
        CancellationToken ct = default)
    {
        var stopped = _taskRegistry.StopTask(task_id);

        var text = stopped
            ? $"任务 {task_id} 已终止。"
            : $"任务 {task_id} 不存在或已完成。";

        return Task.FromResult(ToolResultBuilder.Success().WithText(text).Build());
    }

    private static ToolResult BuildResult(LongRunningTaskResult result, string command)
    {
        var sb = new StringBuilder(512);

        switch (result.State)
        {
            case LongRunningTaskState.Completed:
                sb.AppendLine($"## 任务完成 (耗时 {result.Elapsed.TotalSeconds:F1}s)");
                sb.AppendLine();
                if (!string.IsNullOrEmpty(result.Stdout))
                    sb.AppendLine(result.Stdout);
                if (!string.IsNullOrEmpty(result.Stderr))
                {
                    sb.AppendLine();
                    sb.AppendLine("### stderr");
                    sb.AppendLine(result.Stderr);
                }
                return ToolResultBuilder.Success().WithText(sb.ToString()).Build();

            case LongRunningTaskState.Failed:
                sb.AppendLine($"## 任务失败 (退出码 {result.ExitCode}, 耗时 {result.Elapsed.TotalSeconds:F1}s)");
                sb.AppendLine();
                sb.AppendLine($"**命令**: `{command}`");
                if (!string.IsNullOrEmpty(result.Stderr))
                {
                    sb.AppendLine();
                    sb.AppendLine(result.Stderr);
                }
                return ToolResultBuilder.Error().WithText(sb.ToString()).Build();

            case LongRunningTaskState.TimedOut:
                sb.AppendLine($"## 任务再次超时 (已运行 {result.Elapsed.TotalMinutes:F1}min, 第 {result.RetryCount} 次续期)");
                sb.AppendLine();
                sb.AppendLine($"**命令**: `{command}`");
                sb.AppendLine();
                sb.AppendLine($"任务在续期超时后仍未完成。可以：");
                sb.AppendLine($"- 调用 `continue_long_running_task` 继续等待（task_id: {result.TaskId}）");
                sb.AppendLine($"- 调用 `stop_long_running_task` 放弃此任务（task_id: {result.TaskId}）");
                sb.AppendLine($"- 检查命令是否正确，或拆分为更小的步骤");
                return ToolResultBuilder.Error().WithText(sb.ToString()).Build();

            case LongRunningTaskState.NotFound:
                sb.AppendLine($"## 任务不存在");
                sb.AppendLine(result.Stderr);
                return ToolResultBuilder.Error().WithText(sb.ToString()).Build();

            case LongRunningTaskState.MaxRetriesExceeded:
                sb.AppendLine($"## 已达到最大续期次数 ({result.RetryCount})");
                sb.AppendLine();
                sb.AppendLine($"**命令**: `{command}`");
                sb.AppendLine();
                sb.AppendLine("任务已多次超时，建议：");
                sb.AppendLine("- 检查命令是否可以优化");
                sb.AppendLine("- 拆分为更小的步骤分别执行");
                sb.AppendLine("- 考虑在后台运行此任务");
                return ToolResultBuilder.Error().WithText(sb.ToString()).Build();

            default:
                return ToolResultBuilder.Error().WithText($"未知状态: {result.State}").Build();
        }
    }
}
