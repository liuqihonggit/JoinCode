namespace Core.Agents.Coordinator;

/// <summary>
/// Fork Spawn 中间件 — 构建子智能体选项、Spawn、注册消息代理、Worktree、邮箱轮询
/// </summary>
[Register(typeof(IForkMiddleware))]
public sealed partial class ForkSpawnMiddleware : IForkMiddleware
{
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly IAgentMessageBroker _messageBroker;
    private readonly IAgentWorktreeManager? _worktreeManager;
    private readonly IMailboxPoller? _mailboxPoller;
    private readonly JoinCode.Abstractions.Interfaces.IFileStateCache? _fileStateCache;
    [Inject] private readonly ILogger<ForkSpawnMiddleware>? _logger;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly IClockService _clock;

    public ForkSpawnMiddleware(
        IAgentLifecycleManager lifecycleManager,
        IAgentMessageBroker messageBroker,
        IAgentWorktreeManager? worktreeManager = null,
        IMailboxPoller? mailboxPoller = null,
        JoinCode.Abstractions.Interfaces.IFileStateCache? fileStateCache = null,
        ILogger<ForkSpawnMiddleware>? logger = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null)
    {
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _worktreeManager = worktreeManager;
        _mailboxPoller = mailboxPoller;
        _fileStateCache = fileStateCache;
        _logger = logger;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>Spawn 在缓存初始化之后</summary>

    /// <summary>Spawn 失败应中断管道</summary>
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(ForkContext context, MiddlewareDelegate<ForkContext> next, CancellationToken ct)
    {
        var forkDirective = ForkMessageBuilder.BuildChildMessage(context.Options.TaskDescription);
        context.ForkDirective = forkDirective;

        var cacheSafeParams = context.Options.ShareCache && context.Options.CacheSafeParams is not null
            ? context.Options.CacheSafeParams.Clone()
            : null;
        context.CacheSafeParams = cacheSafeParams;

        var forkTracker = new ProgressTracker(_clock);

        MessageList? initialMessageList = null;
        if (context.Options.ShareContext && context.Options.ParentMessageList is not null && context.Options.ParentMessageList.Count > 0)
        {
            var lastAssistant = context.Options.ParentMessageList.LastOrDefault(m => m.Role == MessageRole.Assistant);
            if (lastAssistant is not null)
            {
                var forkedMessages = ForkMessageBuilder.BuildForkedMessages(context.Options.TaskDescription, lastAssistant);
                initialMessageList = new MessageList();
                foreach (var msg in forkedMessages)
                    initialMessageList.Add(msg);
            }
        }

        var agentOptions = new SubAgentOptions
        {
            AdditionalInstructions = context.Options.SystemPrompt ?? cacheSafeParams?.RenderedSystemPrompt,
            MaxIterations = context.Options.MaxIterations,
            AllowedTools = context.Options.UseExactTools && cacheSafeParams?.ToolNames is not null
                ? cacheSafeParams.ToolNames.ToList()
                : context.Options.AllowedTools,
            DeniedTools = context.Options.DeniedTools,
            PermissionMode = context.Options.PermissionMode.ToString().ToLowerInvariant(),
            CacheSafeParams = cacheSafeParams,
            InitialMessageList = initialMessageList,
            ProgressTracker = forkTracker,
            ReadFileState = initialMessageList is not null
                ? _fileStateCache?.Clone()
                : null,
        };
        context.AgentOptions = agentOptions;

        var agent = await _lifecycleManager.SpawnSubAgentAsync(
            forkDirective, agentOptions, ct).ConfigureAwait(false);

        _messageBroker.RegisterAgent(agent.Id, context.Options.ParentSessionId);

        context.Agent = agent;

        // Worktree 隔离 — 对齐 TS: isolation: "worktree" 在 fork 路径下也生效
        var shouldCreateWorktree = context.Options.IsolationMode == AgentIsolationMode.Worktree
            || (_worktreeManager is not null && _worktreeManager.IsWorktreeIsolationEnabled);
        if (shouldCreateWorktree && _worktreeManager is not null)
        {
            var worktreeCreated = await _worktreeManager.CreateWorktreeAsync(agent.Id, ct).ConfigureAwait(false);
            if (worktreeCreated)
            {
                var session = await _worktreeManager.GetWorktreeSessionAsync(agent.Id, ct).ConfigureAwait(false);
                if (session is not null)
                {
                    var parentCwd = _subAgentContextAccessor.Current?.WorktreePath ?? Environment.CurrentDirectory;
                    var notice = ForkMessageBuilder.BuildWorktreeNotice(parentCwd, session.WorktreePath);
                    agent.AddContext(notice);
                    context.AgentOptions.WorktreePath = session.WorktreePath;
                    context.AgentOptions.WorktreeBranch = session.BranchName;
                }
            }
        }

        // 邮箱轮询
        StartMailboxPollingIfNeeded(agent.Id, context.Options.ParentSessionId);

        _logger?.LogInformation("Fork {ForkId} created for parent session {ParentSessionId}",
            context.ForkId, context.Options.ParentSessionId);

        await next(context, ct).ConfigureAwait(false);
    }

    private void StartMailboxPollingIfNeeded(string agentId, string sessionId)
    {
        if (_mailboxPoller == null) return;

        try
        {
            _mailboxPoller.StartPolling(agentId, sessionId);
            _logger?.LogDebug("Mailbox polling started for fork agent {AgentId}", agentId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to start mailbox polling for fork agent {AgentId}", agentId);
        }
    }
}
