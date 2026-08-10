namespace Tools.Shell;

/// <summary>
/// 编译输出工具处理器 - AI 渐进式阅读编译结果
/// </summary>
[McpToolDispatch(ToolCategory.Build, Optional = true)]
public partial class BuildOutputToolHandlers
{
    [Inject] private readonly IBuildQueueService? _buildQueueService;
    [Inject] private readonly ILogger<BuildOutputToolHandlers>? _logger;

    public BuildOutputToolHandlers(
        IBuildQueueService? buildQueueService = null,
        ILogger<BuildOutputToolHandlers>? logger = null)
    {
        _buildQueueService = buildQueueService;
        _logger = logger;
    }

    /// <summary>
    /// 获取编译输出的指定行范围 — 渐进式阅读编译结果
    /// </summary>
    [McpTool("build_output", "Get build output lines by range for incremental reading", "execution", ConcurrencySafe = true)]
    public Task<ToolResult> BuildOutputAsync(
        [McpToolParameter("Build ID (e.g. b-0001)")] string build_id,
        [McpToolParameter("Start line number (1-based)")] int start_line,
        [McpToolParameter("End line number (inclusive, 0=to end)", Required = false, DefaultValue = "0")] int end_line,
        CancellationToken cancellationToken = default)
    {
        if (_buildQueueService is null)
        {
            var diag = BuildQueueServiceUnavailableDiagnostic();
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build());
        }

        if (string.IsNullOrWhiteSpace(build_id))
        {
            var diag = BuildEmptyBuildIdDiagnostic();
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build());
        }

        if (start_line < 1)
        {
            var diag = BuildInvalidStartLineDiagnostic(start_line);
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build());
        }

        try
        {
            var output = _buildQueueService.GetOutputRange(build_id, start_line, end_line);

            var entry = _buildQueueService.GetBuild(build_id);
            var totalInfo = entry?.Result is not null
                ? $"\n[Build {build_id}, exit={entry.Result.ExitCode}]"
                : "";

            return Task.FromResult(ToolResultBuilder.Success()
                .WithText($"{output}{totalInfo}")
                .Build());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get build output for {BuildId}", build_id);
            return Task.FromResult(ToolExceptionDiagnosticHelper.BuildErrorResult("build_output", ex, _logger, "build_id", build_id));
        }
    }

    /// <summary>
    /// 查询编译队列状态
    /// </summary>
    [McpTool("build_queue_status", "Get build queue status (pending count, current build, recent builds)", "execution", ConcurrencySafe = true)]
    public Task<ToolResult> BuildQueueStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (_buildQueueService is null)
        {
            var diag = BuildQueueServiceUnavailableDiagnostic();
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build());
        }

        try
        {
            var status = _buildQueueService.GetStatus();

            var sb = new StringBuilder();
            sb.AppendLine($"Pending: {status.PendingCount}");
            sb.AppendLine($"Building: {status.IsBuilding}");

            if (status.CurrentBuildId is not null)
            {
                sb.AppendLine($"Current: {status.CurrentBuildId} (agent: {status.CurrentBuildAgentId})");
            }

            if (status.RecentBuilds.Count > 0)
            {
                sb.AppendLine("Recent:");
                foreach (var build in status.RecentBuilds)
                {
                    sb.AppendLine($"  {build.BuildId}: {build.Status} - {build.Request.Command}");
                }
            }

            return Task.FromResult(ToolResultBuilder.Success()
                .WithText(sb.ToString())
                .Build());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get build queue status");
            return Task.FromResult(ToolExceptionDiagnosticHelper.BuildErrorResult("build_queue_status", ex, _logger));
        }
    }

    /// <summary>
    /// 取消编译
    /// </summary>
    [McpTool("build_cancel", "Cancel a build (kill process if building, remove from queue if pending)", "execution", ConcurrencySafe = true)]
    public async Task<ToolResult> BuildCancelAsync(
        [McpToolParameter("Build ID (e.g. b-0001)")] string build_id,
        CancellationToken cancellationToken = default)
    {
        if (_buildQueueService is null)
        {
            var diag = BuildQueueServiceUnavailableDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build();
        }

        if (string.IsNullOrWhiteSpace(build_id))
        {
            var diag = BuildEmptyBuildIdDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build();
        }

        try
        {
            var cancelled = await _buildQueueService.CancelAsync(build_id, cancellationToken).ConfigureAwait(false);

            if (cancelled)
            {
                return ToolResultBuilder.Success().WithText($"Build {build_id} cancelled").Build();
            }

            var notFoundDiag = BuildBuildNotFoundDiagnostic(build_id);
            return ToolResultBuilder.Error()
                .WithText(notFoundDiag.FormattedMessage)
                .WithDiagnostic(notFoundDiag)
                .Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to cancel build {BuildId}", build_id);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("build_cancel", ex, _logger, "build_id", build_id);
        }
    }

    #region Diagnostics

    internal static ToolDiagnostic BuildQueueServiceUnavailableDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "构建队列服务不可用",
            formattedMessage: "Build queue service is not available",
            details: [new DiagnosticDetail("service", "IBuildQueueService")],
            suggestions: ["确认构建队列服务已正确注册"]);

    internal static ToolDiagnostic BuildEmptyBuildIdDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: "build_id is required",
            details: [new DiagnosticDetail("field", "build_id")],
            suggestions: ["提供非空的 build_id 参数"]);

    internal static ToolDiagnostic BuildInvalidStartLineDiagnostic(int startLine) =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: "start_line must be >= 1",
            details:
            [
                new DiagnosticDetail("field", "start_line"),
                new DiagnosticDetail("actual_value", startLine.ToString())
            ],
            suggestions: ["start_line 从 1 开始计数，请提供 >= 1 的值"]);

    internal static ToolDiagnostic BuildGetOutputFailedDiagnostic(string buildId, string errorMessage) =>
        ToolDiagnostic.Create(
            reason: "获取编译输出失败",
            formattedMessage: $"Failed: {errorMessage}",
            details:
            [
                new DiagnosticDetail("build_id", buildId),
                new DiagnosticDetail("error", errorMessage)
            ],
            suggestions: ["检查 build_id 是否正确", "使用 build_queue_status 查看队列状态"]);

    internal static ToolDiagnostic BuildGetStatusFailedDiagnostic(string errorMessage) =>
        ToolDiagnostic.Create(
            reason: "查询编译队列状态失败",
            formattedMessage: $"Failed: {errorMessage}",
            details: [new DiagnosticDetail("error", errorMessage)],
            suggestions: ["检查构建队列服务状态"]);

    internal static ToolDiagnostic BuildBuildNotFoundDiagnostic(string buildId) =>
        ToolDiagnostic.Create(
            reason: "构建未找到或已完成",
            formattedMessage: $"Build {buildId} not found or already completed",
            details: [new DiagnosticDetail("build_id", buildId)],
            suggestions: ["使用 build_queue_status 查看当前队列状态", "确认 build_id 是否正确"]);

    internal static ToolDiagnostic BuildCancelFailedDiagnostic(string buildId, string errorMessage) =>
        ToolDiagnostic.Create(
            reason: "取消构建失败",
            formattedMessage: $"Failed: {errorMessage}",
            details:
            [
                new DiagnosticDetail("build_id", buildId),
                new DiagnosticDetail("error", errorMessage)
            ],
            suggestions: ["检查 build_id 是否正确", "使用 build_queue_status 查看队列状态"]);

    #endregion
}
