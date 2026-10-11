namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 会话保存中间件 — 保存会话 + 遥测记录
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorktreeSessionSaveMiddleware : ServiceEntity, IWorktreeCreateMiddleware {

    /// <summary>
    /// 构造 WorktreeSessionSaveMiddleware 实例，注入延迟加载的管道操作、文件操作服务、时钟服务、遥测服务及日志器
    /// </summary>
    public WorktreeSessionSaveMiddleware(Lazy<IWorktreePipelineOperations> worktreeService, IFileOperationService fs, IClockService clock, ITelemetryService? telemetryService = null, ILogger<WorktreeSessionSaveMiddleware>? logger = null) {
        _worktreeService = worktreeService;
        _fs = fs;
        _clock = clock;
        _telemetryService = telemetryService;
        _logger = logger;
    }
    private readonly Lazy<IWorktreePipelineOperations> _worktreeService;
    private readonly IFileOperationService _fs;
    private readonly ITelemetryService? _telemetryService;
    private readonly ILogger<WorktreeSessionSaveMiddleware>? _logger;
    private readonly IClockService _clock;

    /// <summary>中间件错误处理策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>执行优先级:会话保存最后执行</summary>
    public int Order => 700;

    /// <summary>
    /// 执行会话保存：恢复模式跳过；否则构建会话对象、保存并记录遥测，设置上下文结果
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct) {
        if (context.IsRecovery) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        WorktreeContextEnricher.EnsureAllPaths(context, _fs);

        var opts = context.Options ?? new WorktreeOptions();
        var creationDurationMs = context.CreationStartTime.HasValue
            ? (long)(_clock.GetUtcNow() - context.CreationStartTime.Value).TotalMilliseconds
            : 0;

        var session = new AgentWorktreeSession {
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