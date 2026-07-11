namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 执行中间件 — 执行子智能体并处理结果；后台模式仅标记，由 Manager 启动后台任务
/// 对齐 TS: cleanupWorktreeIfNeeded — fork 完成后清理 worktree
/// </summary>
[Register(typeof(IForkMiddleware))]
public sealed partial class ForkExecutionMiddleware : IForkMiddleware
{
    [Inject] private readonly IAgentLifecycleManager _lifecycleManager;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly IAgentWorktreeManager? _worktreeManager;
    [Inject] private readonly ILogger<ForkExecutionMiddleware>? _logger;

    /// <summary>执行在权限同步之后</summary>

    /// <summary>执行失败应传播异常</summary>
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(ForkContext context, MiddlewareDelegate<ForkContext> next, CancellationToken ct)
    {
        if (context.Agent is null)
        {
            context.FinalState = ForkState.Failed;
            context.FinalResult = "Agent not spawned";
            await CleanupWorktreeIfNeededAsync(context.Agent?.Id, ct).ConfigureAwait(false);
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 后台模式: 仅标记，由 Manager 启动后台任务
        if (context.Options.RunInBackground)
        {
            context.IsBackground = true;
            context.FinalState = ForkState.Running;
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 同步执行
        try
        {
            var result = await _lifecycleManager.ExecuteAsync(context.Agent, ct).ConfigureAwait(false);
            context.ExecutionResult = result;

            if (result.IsSuccess)
            {
                context.FinalState = ForkState.Completed;
                context.FinalResult = result.Output;
                RecordForkMetrics("fork", true);
                _logger?.LogInformation("Fork {ForkId} completed successfully", context.ForkId);
            }
            else
            {
                context.FinalState = ForkState.Failed;
                context.FinalResult = result.Error ?? "Unknown error";
                RecordForkMetrics("fork", false);
                _logger?.LogWarning("Fork {ForkId} failed: {Error}", context.ForkId, result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            context.FinalState = ForkState.Cancelled;
            context.FinalResult = null;
            RecordForkMetrics("fork_cancelled", false);
            _logger?.LogInformation("Fork {ForkId} was cancelled", context.ForkId);
        }
        catch (Exception ex)
        {
            context.FinalState = ForkState.Failed;
            context.FinalResult = ex.Message;
            RecordForkMetrics("fork_error", false);
            _logger?.LogError(ex, "Fork {ForkId} failed with exception", context.ForkId);
        }
        finally
        {
            await CleanupWorktreeIfNeededAsync(context.Agent?.Id, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task CleanupWorktreeIfNeededAsync(string? agentId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(agentId) || _worktreeManager is null) return;

        try
        {
            var cleanupDetail = await _worktreeManager.CleanupWorktreeAsync(agentId, ct).ConfigureAwait(false);
            if (cleanupDetail.Kept)
            {
                _logger?.LogInformation("Fork agent {AgentId} worktree kept: {Path} (reason: {Reason})",
                    agentId, cleanupDetail.WorktreePath, cleanupDetail.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Fork agent {AgentId} worktree cleanup failed", agentId);
        }
    }

    private void RecordForkMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("fork.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Fork operation count");
}
