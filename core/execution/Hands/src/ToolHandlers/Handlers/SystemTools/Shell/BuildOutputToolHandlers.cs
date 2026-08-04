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
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText("Build queue service is not available")
                .Build());
        }

        if (string.IsNullOrWhiteSpace(build_id))
        {
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText("build_id is required")
                .Build());
        }

        if (start_line < 1)
        {
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText("start_line must be >= 1")
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
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText($"Failed: {ex.Message}")
                .Build());
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
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText("Build queue service is not available")
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
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText($"Failed: {ex.Message}")
                .Build());
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
            return ToolResultBuilder.Error()
                .WithText("Build queue service is not available")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(build_id))
        {
            return ToolResultBuilder.Error()
                .WithText("build_id is required")
                .Build();
        }

        try
        {
            var cancelled = await _buildQueueService.CancelAsync(build_id, cancellationToken).ConfigureAwait(false);

            return cancelled
                ? ToolResultBuilder.Success().WithText($"Build {build_id} cancelled").Build()
                : ToolResultBuilder.Error().WithText($"Build {build_id} not found or already completed").Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to cancel build {BuildId}", build_id);
            return ToolResultBuilder.Error()
                .WithText($"Failed: {ex.Message}")
                .Build();
        }
    }
}
