namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 会话保存中间件 — 保存会话 + 遥测记录
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware))]
public sealed partial class WorktreeSessionSaveMiddleware : IWorktreeCreateMiddleware
{
    [Inject] private readonly Lazy<IWorktreePipelineOperations> _worktreeService;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly ILogger<WorktreeSessionSaveMiddleware>? _logger;
    [Inject] private readonly IClockService _clock;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        if (context.IsRecovery)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var opts = context.Options ?? new WorktreeOptions();
        var creationDurationMs = context.CreationStartTime.HasValue
            ? (long)(_clock.GetUtcNow() - context.CreationStartTime.Value).TotalMilliseconds
            : 0;

        var session = new AgentWorktreeSession
        {
            AgentId = context.AgentId,
            OriginalCwd = context.OriginalCwd,
            WorktreePath = context.WorktreePath,
            BranchName = context.BranchName,
            GitRootPath = context.GitRoot,
            OriginalBranch = context.OriginalBranch,
            BaseCommitSha = context.BaseCommitSha,
            CreatedAt = _clock.GetUtcNow(),
            Existed = false,
            CreationDurationMs = creationDurationMs,
            SparsePaths = opts.SparsePaths?.ToList()
        };

        await _worktreeService.Value.SaveSessionAsync(session).ConfigureAwait(false);

        _logger?.LogInformation(
            "成功创建 worktree: {WorktreePath}, Branch: {BranchName}, Duration: {DurationMs}ms",
            context.WorktreePath, context.BranchName, creationDurationMs);

        _telemetryService?.RecordCount("worktree.operation.count",
            new Dictionary<string, string> { ["operation"] = "create", ["success"] = "true" },
            "count", "Worktree operation count");

        context.Session = session;
        context.CreationDurationMs = creationDurationMs;
        context.Result = WorktreeCreateResult.SuccessResult(session);

        await next(context, ct).ConfigureAwait(false);
    }
}
