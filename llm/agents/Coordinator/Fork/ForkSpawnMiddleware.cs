namespace Core.Agents.Coordinator;

/// <summary>
/// Fork Spawn 中间件 — 构建子智能体选项、Spawn、注册消息代理、Worktree、邮箱轮询
/// </summary>
[Register(typeof(IForkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ForkSpawnMiddleware : ServiceEntity, IForkMiddleware {
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly IMailbox _messageBroker;
    private readonly IAgentWorktreeManager? _worktreeManager;
    private readonly IMailboxPoller? _mailboxPoller;
    private readonly JoinCode.Abstractions.Interfaces.IFileStateCache? _fileStateCache;
    private readonly IHotSpotSpawnIntegration? _hotSpotIntegration;
    private readonly JoinCode.Abstractions.Interfaces.IDeferredMailService? _deferredMailService;
    private readonly ILogger<ForkSpawnMiddleware>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IClockService _clock;

    /// <summary>
    /// 初始化 Fork Spawn 中间件
    /// </summary>
    /// <param name="lifecycleManager">智能体生命周期管理器</param>
    /// <param name="messageBroker">消息邮箱</param>
    /// <param name="worktreeManager">工作树管理器</param>
    /// <param name="mailboxPoller">邮箱轮询器</param>
    /// <param name="fileStateCache">文件状态缓存</param>
    /// <param name="hotSpotIntegration">热点 Spawn 集成</param>
    /// <param name="deferredMailService">延迟邮件服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="subAgentContextAccessor">子智能体上下文访问器</param>
    /// <param name="clock">时钟服务</param>
    public ForkSpawnMiddleware(
        IAgentLifecycleManager lifecycleManager,
        IMailbox messageBroker,
        IAgentWorktreeManager? worktreeManager = null,
        IMailboxPoller? mailboxPoller = null,
        JoinCode.Abstractions.Interfaces.IFileStateCache? fileStateCache = null,
        IHotSpotSpawnIntegration? hotSpotIntegration = null,
        JoinCode.Abstractions.Interfaces.IDeferredMailService? deferredMailService = null,
        ILogger<ForkSpawnMiddleware>? logger = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null) {
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _worktreeManager = worktreeManager;
        _mailboxPoller = mailboxPoller;
        _fileStateCache = fileStateCache;
        _hotSpotIntegration = hotSpotIntegration;
        _deferredMailService = deferredMailService;
        _logger = logger;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>Spawn 在缓存初始化之后</summary>

    /// <summary>Spawn 失败应中断管道</summary>

    /// <summary>
    /// 异步执行 Spawn 逻辑：构建子智能体选项、Spawn、注册消息代理、Worktree 隔离与邮箱轮询
    /// </summary>
    /// <param name="context">Fork 上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(ForkContext context, MiddlewareDelegate<ForkContext> next, CancellationToken ct) {
        var forkDirective = ForkMessageBuilder.BuildChildMessage(context.Options.TaskDescription);
        context.ForkDirective = forkDirective;

        var cacheSafeParams = context.Options.ShareCache && context.Options.CacheSafeParams is not null
            ? context.Options.CacheSafeParams.Clone()
            : null;
        context.CacheSafeParams = cacheSafeParams;

        var forkTracker = new ProgressTracker(_clock);

        MessageList? initialMessageList = null;
        if (context.Options.ShareContext && context.Options.ParentMessageList is not null && context.Options.ParentMessageList.Count > 0) {
            var lastAssistant = context.Options.ParentMessageList.LastOrDefault(m => m.Role == MessageRole.Assistant);
            if (lastAssistant is not null) {
                var forkedMessages = ForkMessageBuilder.BuildForkedMessages(context.Options.TaskDescription, lastAssistant);
                initialMessageList = new MessageList();
                foreach (var msg in forkedMessages)
                    initialMessageList.Add(msg);
            }
        }

        var agentOptions = new SubAgentOptions {
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
            forkDirective, agentOptions, ct, context.Options.ParentSessionId).ConfigureAwait(false);

        _messageBroker.RegisterAgent(agent.ObjectId.UniqueId, context.Options.ParentSessionId);

        context.Agent = agent;

        // Worktree 隔离 — 对齐 TS: isolation: "worktree" 在 fork 路径下也生效
        var perAgentIsolation = context.Options.IsolationMode == AgentIsolationMode.Worktree;
        var globalIsolation = _worktreeManager is not null && _worktreeManager.IsWorktreeIsolationEnabled;
        if ((perAgentIsolation || globalIsolation) && _worktreeManager is not null) {
            AgentWorktreeSession? session = null;
            if (perAgentIsolation) {
                session = await _worktreeManager.CreateWorktreeForAgentAsync(agent.ObjectId.UniqueId, ct).ConfigureAwait(false);
            } else {
                var worktreeCreated = await _worktreeManager.CreateWorktreeAsync(agent.ObjectId.UniqueId, ct).ConfigureAwait(false);
                if (worktreeCreated) {
                    session = await _worktreeManager.GetWorktreeSessionAsync(agent.ObjectId.UniqueId, ct).ConfigureAwait(false);
                }
            }

            if (session is not null) {
                var parentCwd = _subAgentContextAccessor.Current?.WorktreePath ?? Environment.CurrentDirectory;
                var notice = ForkMessageBuilder.BuildWorktreeNotice(parentCwd, session.WorktreePath);
                ((AgentBase)agent).AddContext(notice);
                context.AgentOptions.WorktreePath = session.WorktreePath;
                context.AgentOptions.WorktreeBranch = session.BranchName;
            }
        }

        // 邮箱轮询
        StartMailboxPollingIfNeeded(agent.ObjectId.UniqueId, context.Options.ParentSessionId);

        // 热点集成 — 注册文件写入监听器 + 设置契约变更通知队列
        if (_hotSpotIntegration is not null) {
            var captainId = _subAgentContextAccessor.Current?.AgentId ?? "main";
            _hotSpotIntegration.EnsureListenersRegistered(captainId);
            if (agent is AgentBase agentBase) {
                agentBase.ContractChangeNotifications = _hotSpotIntegration.GetOrCreateNotificationQueue(agent.ObjectId.UniqueId);
                agentBase.DeferredMailService = _deferredMailService;
            }
        }

        _logger?.LogInformation("Fork {ForkId} created for parent session {ParentSessionId}",
            context.ForkId, context.Options.ParentSessionId);

        await next(context, ct).ConfigureAwait(false);
    }

    private void StartMailboxPollingIfNeeded(string agentId, string sessionId) {
        if (_mailboxPoller == null) return;

        try {
            _mailboxPoller.StartPolling(agentId, sessionId);
            _logger?.LogDebug("Mailbox polling started for fork agent {AgentId}", agentId);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to start mailbox polling for fork agent {AgentId}", agentId);
        }
    }
}