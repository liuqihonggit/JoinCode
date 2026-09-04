
namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 抽象基类 — 持有 LLM 对话循环、生命周期控制、任务/上下文/预算/输出
/// 子类（CoordinatorAgent、ExecutorAgent、ReasoningAgent）继承此类，自动获得对话能力
/// 压缩管线在阶段2 通过 IChatContextManager 内聚到此
/// </summary>
public class AgentBase : Entity, IAgent
{
    protected readonly IQueryEngine _queryEngine;
    protected readonly ILogger? _logger;
    protected readonly IClockService _clock;
    protected readonly List<string> _context;
    protected readonly CancellationTokenSource _cts;
#pragma warning disable JCC4005 // SemaphoreSlim 在 OnDispose() 中释放，分析器无法追踪间接调用路径
    protected readonly AsyncLock _pauseLock;
#pragma warning restore JCC4005
    protected JoinCode.Abstractions.LLM.Chat.CacheSafeParams? _lastCacheSafeParams;

    // === 身份（ObjectId/UniqueId/CreatedAt 继承自 Entity）===
    public string Name { get; }
    public AgentRole Role { get; }
    public ExecutorVariant? Variant { get; }
    public ObjectId? ParentObjectId { get; init; }

    // === 任务 ===
    public string Task { get; }
    /// <summary>
    /// 当前用户输入 — 主代理每轮对话前设置，优先于 Task 作为 prompt
    /// 子代理不设置（用 Task）
    /// </summary>
    public string? CurrentInput { get; set; }
    public SubAgentOptions Options { get; }
    public SubAgentContext? Context { get; }
    public TaskExecutionStatus Status { get; set; }
    public TaskExecutionStatus State { get; set; }
    public CancellationTokenSource? CancellationTokenSource { get; set; }

    // === 上下文 ===
    public MessageList ChatHistory { get; } = new();
    public bool FreshContext { get; init; }

    // === 配置 ===
    public string? SystemPrompt { get; init; }
    public string? Instruction { get; set; }

    // === 预算 ===
    public int? TokenBudget { get; init; }
    public int TokensUsed { get; set; }
    public int TurnsCompleted { get; set; }

    // === Goal绑定 ===
    public string? GoalId { get; init; }
    public string? GraphNodeId { get; init; }

    // === 输出 ===
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public string[]? Routes { get; set; }

    protected int _executionCount;

    /// <summary>
    /// 对话上下文管理器 — 每个 Agent 实例独立持有，包含压缩管线
    /// null 表示使用裸 MessageList（兼容旧行为），非 null 表示通过 ContextManager 管理对话
    /// 子类继承 AgentBase 自动获得此字段和压缩能力
    /// </summary>
    protected IChatContextManager? ContextManager;

    /// <summary>
    /// 用户输入转发队列 — 子代理运行期间用户追加的输入，每轮 LLM 调用前主动 TryDrain 消费
    /// null 表示不支持用户转发（默认）；由 ForkSpawnMiddleware 在创建子代理后注入
    /// </summary>
    public JoinCode.Abstractions.Interfaces.IAgentInputForwardQueue? InputForwardQueue { get; set; }

    /// <summary>
    /// T5.0: 契约变更通知队列 — 每轮 LLM 调用前消费，收到 ContractChanged 后通知 Worker 已同步主干
    /// 外部（MailboxPoller/AgentCoordinator）负责往队列里塞通知，AgentBase.DrainPendingUserInputs 消费
    /// null 表示未接入契约变更通知（默认）
    /// </summary>
    public ConcurrentQueue<string> ContractChangeNotifications { get; set; } = new();

    /// <summary>
    /// 延迟邮件服务 — 每轮 LLM 调用前消费到期延迟邮件, 注入 ChatHistory
    /// null 表示未接入延迟邮件(默认); 由 ForkSpawnMiddleware 注入
    /// </summary>
    public JoinCode.Abstractions.Interfaces.IDeferredMailService? DeferredMailService { get; set; }

    /// <summary>
    /// 输出 channel 管理器 — AgentBase.ExecuteStreamAsync 中统一写入，前台拉取显示
    /// null 表示不支持输出 channel（默认）；由 AgentServiceImpl 在创建子代理后注入
    /// 主代理和子代理都通过此属性统一输出，在父类 AgentBase 上一处实现
    /// </summary>
    public JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager? OutputChannelManager { get; set; }

    /// <summary>
    /// AgentBase 构造函数 — 子类通过 base(...) 委托
    /// </summary>
    public AgentBase(
        string task,
        SubAgentOptions? options,
        IQueryEngine queryEngine,
        ILogger? logger,
        IClockService? clock = null,
        string? name = null,
        AgentRole role = AgentRole.Executor,
        ExecutorVariant? variant = null,
        ObjectId? parentObjectId = default,
        string? systemPrompt = null,
        string? instruction = null,
        bool freshContext = false,
        int? tokenBudget = null,
        string? goalId = null,
        string? graphNodeId = null,
        ObjectId sessionId = default,
        IChatContextManager? contextManager = null,
        string? customUniqueId = null)
        : base(ObjectType.Agent, sessionId, customUniqueId: customUniqueId)
    {
        Task = task;
        Name = name ?? UniqueId;
        Role = role;
        Variant = variant;
        ParentObjectId = parentObjectId;
        SystemPrompt = systemPrompt;
        Instruction = instruction;
        FreshContext = freshContext;
        TokenBudget = tokenBudget;
        GoalId = goalId;
        GraphNodeId = graphNodeId;
        Options = options ?? new SubAgentOptions();
        _queryEngine = queryEngine;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _context = new List<string>();
        _cts = new CancellationTokenSource();
        _pauseLock = new AsyncLock(nameof(AgentBase));
        Status = TaskExecutionStatus.Pending;
        _executionCount = 0;
        ContextManager = contextManager;
        Context = new SubAgentContext
        {
            AgentId = UniqueId,
            Role = role,
            Variant = variant,
            Task = task,
            AllowedTools = Options.AllowedTools,
            DeniedTools = Options.DeniedTools,
            SubagentName = Options.SubagentName,
            IsBuiltIn = Options.IsBuiltIn,
            DisplayName = Options.DisplayName,
            PermissionMode = Options.PermissionMode
        };
    }

    /// <summary>
    /// 惰性释放 — 持久化服务确认消息全部写入后才调用
    /// </summary>
    protected override void OnDispose()
    {
        _cts.Dispose();
        _pauseLock.Dispose();
    }

    /// <summary>
    /// 添加上下文信息
    /// </summary>
    public virtual void AddContext(string context)
    {
        _context.Add(context);
    }

    /// <summary>
    /// 执行Agent任务 — 子类可重写以定制执行逻辑
    /// </summary>
    public virtual async System.Threading.Tasks.Task<SubAgentResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var linkedToken = linkedCts.Token;

        StartedAt = _clock.GetUtcNow();
        Status = TaskExecutionStatus.Running;
        _executionCount++;

        if (_executionCount > Options.MaxIterations)
        {
            _logger?.LogWarning("[Agent {AgentId}] 已达最大迭代次数 {MaxIterations},停止执行", UniqueId, Options.MaxIterations);
            CompletedAt = _clock.GetUtcNow();
            Status = TaskExecutionStatus.Completed;
            return new SubAgentResult
            {
                AgentId = UniqueId,
                IsSuccess = true,
                Output = $"已达最大迭代次数 {Options.MaxIterations}",
                ExecutionTimeMs = 0,
            };
        }

        if (Context is not null)
        {
            Context.StartedAt = StartedAt;
            Context.Status = AgentStatus.Running;
        }

        using var scope = Context?.EnterScopeWithCwd(Options.WorktreePath);

        try
        {
            _logger?.LogInformation(AgentCoordinatorConstants.LogMessages.SubAgentStartExecute, AgentCoordinatorConstants.LogMessages.SubAgentPrefix, UniqueId, _executionCount);

            var prompt = BuildPrompt();

            MessageList chatHistory;
            if (Options.InitialMessageList is not null && Options.InitialMessageList.Count > 0)
            {
                chatHistory = Options.InitialMessageList;
            }
            else
            {
                chatHistory = new MessageList();
                var systemMessage = !string.IsNullOrWhiteSpace(SystemPrompt ?? Options.SystemPrompt)
                    ? (SystemPrompt ?? Options.SystemPrompt!)
                    : string.Format(AgentCoordinatorConstants.SystemPrompts.SubAgentSystemMessage, Task);
                chatHistory.AddSystemMessage(systemMessage);
            }

            DrainPendingUserInputs(chatHistory);

            if (!string.IsNullOrWhiteSpace(Options.InitialPrompt))
            {
                chatHistory.AddUserMessage(Options.InitialPrompt);
            }

            // 每轮重注入 criticalSystemReminder — 对齐 TS 原版 re-injected at every user turn
            // 作为 user message 注入到消息流,保持紧迫感(如 verification agent 的 "CRITICAL: VERIFICATION-ONLY")
            if (!string.IsNullOrWhiteSpace(Options.CriticalSystemReminder))
            {
                chatHistory.AddUserMessage(Options.CriticalSystemReminder);
            }

            var responseBuilder = new StringBuilder();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var queryOptions = BuildChatOptions();

            await foreach (var chunk in _queryEngine.QueryAsync(prompt, chatHistory, queryOptions, linkedToken))
            {
                if (Status == TaskExecutionStatus.Paused)
                {
                    _logger?.LogInformation("[{AgentType} {AgentId}] 进入暂停等待状态", GetType().Name, UniqueId);
                    var pauseStart = _clock.GetUtcNow();

                    try
                    {
                        using (await _pauseLock.TryLockAsync(linkedToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_pauseLock.Name}' 等待超时")) { }

                        var pauseDuration = _clock.GetUtcNow() - pauseStart;
                        _logger?.LogInformation("[{AgentType} {AgentId}] 暂停结束，等待时长 {PauseDurationMs}ms", GetType().Name, UniqueId, pauseDuration.TotalMilliseconds);
                    }
                    catch (TimeoutException)
                    {
                        _logger?.LogWarning("[{AgentType} {AgentId}] 暂停等待超时（30秒），自动恢复执行", GetType().Name, UniqueId);
                        Status = TaskExecutionStatus.Running;
                    }
                }

                if (chunk.Type == AgentStreamChunkType.Content)
                {
                    responseBuilder.Append(chunk.Content);
                }
                else if (chunk.Type == AgentStreamChunkType.Complete && chunk.CacheSafeParams is not null)
                {
                    _lastCacheSafeParams = chunk.CacheSafeParams;
                    if (Context is not null)
                    {
                        Context.CacheSafeParams = chunk.CacheSafeParams;
                    }
                }
            }

            stopwatch.Stop();
            CompletedAt = _clock.GetUtcNow();
            Status = TaskExecutionStatus.Completed;

            if (Context is not null)
            {
                Context.CompletedAt = CompletedAt;
                Context.Status = AgentStatus.Completed;
            }

            var output = responseBuilder.ToString();
            Output = output;

            _logger?.LogInformation("[Agent {AgentId}] 任务执行完成，耗时{ElapsedMs}ms", UniqueId, stopwatch.ElapsedMilliseconds);

            return new SubAgentResult
            {
                AgentId = UniqueId,
                IsSuccess = true,
                Output = output,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                CacheSafeParams = _lastCacheSafeParams
            };
        }
        catch (OperationCanceledException)
        {
            CompletedAt = _clock.GetUtcNow();
            Status = TaskExecutionStatus.Cancelled;

            if (Context is not null)
            {
                Context.CompletedAt = CompletedAt;
                Context.Status = AgentStatus.Stopped;
            }

            throw;
        }
        catch (Exception ex)
        {
            CompletedAt = _clock.GetUtcNow();
            Status = TaskExecutionStatus.Failed;
            ErrorMessage = ex.Message;

            if (Context is not null)
            {
                Context.CompletedAt = CompletedAt;
                Context.Status = AgentStatus.Failed;
            }

            _logger?.LogError(ex, "[Agent {AgentId}] 任务执行失败", UniqueId);

            return new SubAgentResult
            {
                AgentId = UniqueId,
                IsSuccess = false,
                Output = string.Empty,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// 流式执行Agent任务 — 子类可重写以定制流式逻辑
    /// </summary>
    public virtual async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var linkedToken = linkedCts.Token;

        StartedAt = _clock.GetUtcNow();
        Status = TaskExecutionStatus.Running;
        _executionCount++;

        if (_executionCount > Options.MaxIterations)
        {
            _logger?.LogWarning("[Agent {AgentId}] 已达最大迭代次数 {MaxIterations},停止执行", UniqueId, Options.MaxIterations);
            CompletedAt = _clock.GetUtcNow();
            Status = TaskExecutionStatus.Completed;
            yield return new AgentStreamChunk { Type = AgentStreamChunkType.Complete, Content = $"已达最大迭代次数 {Options.MaxIterations}", AgentId = UniqueId };
            yield break;
        }

        if (Context is not null)
        {
            Context.StartedAt = StartedAt;
            Context.Status = AgentStatus.Running;
        }

        using var scope = Context?.EnterScopeWithCwd(Options.WorktreePath);

        var prompt = BuildPrompt();

        MessageList chatHistory;
        if (ContextManager is not null)
        {
            chatHistory = await ContextManager.GetMessageListAsync(linkedToken).ConfigureAwait(false);
            if (chatHistory.Count > 0 && chatHistory[chatHistory.Count - 1].Role == MessageRole.User)
            {
                chatHistory.RemoveAt(chatHistory.Count - 1);
            }
        }
        else if (Options.InitialMessageList is not null && Options.InitialMessageList.Count > 0)
        {
            chatHistory = Options.InitialMessageList;
        }
        else
        {
            chatHistory = new MessageList();
            var systemMessage = !string.IsNullOrWhiteSpace(SystemPrompt ?? Options.SystemPrompt)
                ? (SystemPrompt ?? Options.SystemPrompt!)
                : string.Format(AgentCoordinatorConstants.SystemPrompts.SubAgentSystemMessage, Task);
            chatHistory.AddSystemMessage(systemMessage);
        }

        DrainPendingUserInputs(chatHistory);

        if (!string.IsNullOrWhiteSpace(Options.InitialPrompt))
        {
            chatHistory.AddUserMessage(Options.InitialPrompt);
        }

        // 每轮重注入 criticalSystemReminder — 对齐 TS 原版 re-injected at every user turn
        if (!string.IsNullOrWhiteSpace(Options.CriticalSystemReminder))
        {
            chatHistory.AddUserMessage(Options.CriticalSystemReminder);
        }

        var responseBuilder = new StringBuilder();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var queryOptions = BuildChatOptions();
        var succeeded = true;
        string? errorMessage = null;

        IAsyncEnumerable<QueryStreamChunk> queryStream = queryOptions is not null
            ? _queryEngine.QueryAsync(prompt, chatHistory, queryOptions, linkedToken)
            : _queryEngine.QueryAsync(prompt, chatHistory, linkedToken);

        await foreach (var chunk in queryStream.ConfigureAwait(false))
        {
            if (Status == TaskExecutionStatus.Paused)
            {
                try
                {
                    using (await _pauseLock.TryLockAsync(linkedToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_pauseLock.Name}' 等待超时")) { }
                }
                catch (TimeoutException)
                {
                    Status = TaskExecutionStatus.Running;
                }
            }

            if (chunk.Type == AgentStreamChunkType.Content)
            {
                responseBuilder.Append(chunk.Content);
            }
            else if (chunk.Type == AgentStreamChunkType.Complete && chunk.CacheSafeParams is not null)
            {
                _lastCacheSafeParams = chunk.CacheSafeParams;
                if (Context is not null)
                {
                    Context.CacheSafeParams = chunk.CacheSafeParams;
                }
            }
            else if (chunk.Type == AgentStreamChunkType.Error)
            {
                succeeded = false;
                errorMessage = chunk.Content;
            }

            if (OutputChannelManager is not null && chunk.Type == AgentStreamChunkType.Content && !string.IsNullOrEmpty(chunk.Content))
            {
                OutputChannelManager.Write(UniqueId, Options.DisplayName ?? Name, chunk.Content, JoinCode.Abstractions.Interfaces.AgentOutputChunkType.Text);
            }

            yield return new AgentStreamChunk
            {
                Type = chunk.Type,
                Content = chunk.Content,
                ThinkingContent = chunk.ThinkingContent,
                ToolName = chunk.ToolName,
                ToolCallId = chunk.ToolCallId,
                ToolArguments = chunk.ToolArguments,
                ToolCallNumber = chunk.ToolCallNumber,
                ToolResult = chunk.ToolResult,
                ToolResultText = chunk.ToolResultText,
                IsToolError = chunk.IsToolError,
                StructuredPatch = chunk.StructuredPatch,
                ProgressMessage = chunk.ProgressMessage,
                ProgressType = chunk.ProgressType,
                LoopTriggerCount = chunk.LoopTriggerCount,
                LoopStartIndex = chunk.LoopStartIndex,
                ExecutionTimeMs = chunk.ExecutionTimeMs,
                Usage = chunk.Usage,
                ModelId = chunk.ModelId,
                AgentId = UniqueId
            };
        }

        stopwatch.Stop();
        CompletedAt = _clock.GetUtcNow();
        Status = succeeded ? TaskExecutionStatus.Completed : TaskExecutionStatus.Failed;

        if (Context is not null)
        {
            Context.CompletedAt = CompletedAt;
            Context.Status = succeeded ? AgentStatus.Completed : AgentStatus.Failed;
        }

        var finalOutput = succeeded ? responseBuilder.ToString() : errorMessage;
        if (succeeded) Output = finalOutput;
        else ErrorMessage = errorMessage;

        yield return new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            Content = finalOutput,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            AgentId = UniqueId
        };
    }

    /// <summary>
    /// 暂停LLM循环 — 子类可重写以扩展暂停逻辑
    /// </summary>
    public virtual void Pause()
    {
        if (Status == TaskExecutionStatus.Running)
        {
            Status = TaskExecutionStatus.Paused;
            _logger?.LogInformation("[{AgentType} {AgentId}] 任务已暂停，等待恢复信号", GetType().Name, UniqueId);
        }
    }

    /// <summary>
    /// 恢复LLM循环 — 子类可重写以扩展恢复逻辑
    /// </summary>
    public virtual void Resume()
    {
        if (Status == TaskExecutionStatus.Paused)
        {
            Status = TaskExecutionStatus.Running;
            _logger?.LogInformation("[{AgentType} {AgentId}] 任务已恢复，释放暂停锁", GetType().Name, UniqueId);
        }
    }

    /// <summary>
    /// 取消LLM循环 — 子类可重写以扩展取消逻辑
    /// </summary>
    public virtual void Cancel()
    {
        _cts.Cancel();
        Status = TaskExecutionStatus.Cancelled;
        _logger?.LogInformation("[Agent {AgentId}] 任务已取消", UniqueId);
    }

    /// <summary>
    /// 重置Agent状态（用于重试）— 子类可重写以扩展重置逻辑
    /// </summary>
    public virtual void Reset()
    {
        Status = TaskExecutionStatus.Pending;
        StartedAt = null;
        CompletedAt = null;
        _logger?.LogInformation("[Agent {AgentId}] 状态已重置", UniqueId);
    }

    /// <summary>
    /// 消费用户转发输入队列 — 每轮 LLM 调用前调用，将待处理用户输入追加到 ChatHistory
    /// 用户在子代理运行期间发送的消息，由主代理转发到 IAgentInputForwardQueue，子代理主动消费
    /// </summary>
    protected void DrainPendingUserInputs(MessageList chatHistory)
    {
        var hasTaskInput = false;

        if (InputForwardQueue is not null)
        {
            var pendingInputs = InputForwardQueue.TryDrain(UniqueId);
            if (pendingInputs.Count > 0)
            {
                hasTaskInput = true;
                foreach (var input in pendingInputs)
                {
                    chatHistory.AddUserMessage($"[用户追加输入] {input}");
                }
                _logger?.LogInformation("[Agent {AgentId}] 消费 {Count} 条用户转发输入", UniqueId, pendingInputs.Count);
            }
        }

        // T5.0: 消费契约变更通知 — 收到 ContractChanged 后通知 Worker 已同步主干，继续工作
        {
            var changeCount = 0;
            while (ContractChangeNotifications.TryDequeue(out var changeContent))
            {
                hasTaskInput = true;
                chatHistory.AddUserMessage($"[契约变更通知] 队长已改热文件并 push: {changeContent}。已同步主干，请继续你的任务，保留本地半成品。");
                changeCount++;
            }
            if (changeCount > 0)
            {
                _logger?.LogInformation("[Agent {AgentId}] 消费 {Count} 条契约变更通知", UniqueId, changeCount);
            }
        }

        // 延迟邮件: 有任务输入时只消费到期邮件(TickTurns), 空闲时立即读取全部(FlushOnTaskEnd)
        if (DeferredMailService is not null)
        {
            var mails = hasTaskInput
                ? DeferredMailService.TickTurns(UniqueId)
                : DeferredMailService.FlushOnTaskEnd(UniqueId);
            foreach (var mail in mails)
            {
                chatHistory.AddUserMessage($"[延迟邮件] {mail.Subject}: {mail.Body}");
            }
            if (mails.Count > 0)
            {
                _logger?.LogInformation("[Agent {AgentId}] 消费 {Count} 封延迟邮件({Mode})", UniqueId, mails.Count, hasTaskInput ? "到期" : "空闲立即");
            }
        }
    }

    /// <summary>
    /// 构建提示词 — 主代理优先用 CurrentInput，子代理用 Task
    /// </summary>
    protected virtual string BuildPrompt()
    {
        if (!string.IsNullOrEmpty(CurrentInput))
            return CurrentInput;

        var sb = new StringBuilder();
        sb.AppendLine($"任务: {Task}");

        if (_context.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("上下文信息:");
            foreach (var ctx in _context)
            {
                sb.AppendLine($"- {ctx}");
            }
        }

        if (!string.IsNullOrEmpty(Options.AdditionalInstructions))
        {
            sb.AppendLine();
            sb.AppendLine($"额外指令: {Options.AdditionalInstructions}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建聊天选项 — 子类可重写以定制选项
    /// </summary>
    protected virtual QueryOptions? BuildChatOptions()
    {
        var hasAllowed = Options.AllowedTools is not null && Options.AllowedTools.Count > 0;
        var hasDenied = Options.DeniedTools is not null && Options.DeniedTools.Count > 0;
        var hasCacheSafeParams = Options.CacheSafeParams is not null;
        var hasContentReplacementState = Options.ContentReplacementState is not null;
        var hasModelName = !string.IsNullOrEmpty(Options.ModelName);
        var effortLevel = JoinCode.Abstractions.LLM.EffortLevelHelper.ParseEffortLevel(Options.Effort);

        if (!hasAllowed && !hasDenied && !hasCacheSafeParams && !hasContentReplacementState && effortLevel is null && !hasModelName)
            return null;

        return new QueryOptions
        {
            AllowedTools = Options.AllowedTools ?? [],
            DeniedTools = Options.DeniedTools ?? [],
            CacheSafeParams = Options.CacheSafeParams,
            ProgressTracker = Options.ProgressTracker,
            ContentReplacementState = Options.ContentReplacementState,
            SessionId = Options.SessionId,
            EffortLevel = effortLevel,
            ModelId = Options.ModelName,
        };
    }

    /// <summary>
    /// 生成唯一 Agent Id
    /// </summary>
    public static string GenerateId() => $"agent-{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// 获取当前会话作用域 — 通过 SessionContext.AsyncLocal 隐式定位
    /// </summary>
    private static SessionScope? GetCurrentScope()
    {
        var sessionId = SessionContext.Current;
        if (sessionId is null) return null;
        return SessionRouter.GetScope(sessionId.Value);
    }

    /// <summary>
    /// 获取当前会话的所有主 Agent (Role=Coordinator) — 替代 AgentRegistry.GetMainAgents
    /// </summary>
    public static IReadOnlyList<AgentBase> GetMainAgents()
    {
        var scope = GetCurrentScope();
        if (scope is null) return [];
        return scope.GetAll<AgentBase>().Where(a => a.Role == AgentRole.Coordinator).ToList();
    }

    /// <summary>
    /// 按 ObjectId 获取 Agent — 仅在当前会话作用域内查找, 替代 AgentRegistry.Get
    /// </summary>
    public static AgentBase? GetById(ObjectId id)
    {
        var scope = GetCurrentScope();
        return scope?.Resolve<AgentBase>(id);
    }

    /// <summary>
    /// 获取指定主 Agent 的所有子 Agent — 通过 ParentObjectId 过滤, 替代 AgentRegistry.GetSubAgents
    /// </summary>
    public static IReadOnlyList<AgentBase> GetSubAgents(ObjectId mainAgentId)
    {
        var scope = GetCurrentScope();
        if (scope is null) return [];
        return scope.GetAll<AgentBase>().Where(a => a.ParentObjectId == mainAgentId).ToList();
    }

    /// <summary>
    /// 按 GoalId 获取 Agent — 替代 AgentRegistry.GetByGoalId
    /// </summary>
    public static IReadOnlyList<AgentBase> GetByGoalId(string goalId)
    {
        var scope = GetCurrentScope();
        if (scope is null) return [];
        return scope.GetAll<AgentBase>().Where(a => a.GoalId == goalId).ToList();
    }

    /// <summary>
    /// 按状态获取 Agent — 替代 AgentRegistry.GetByStatus
    /// </summary>
    public static IReadOnlyList<AgentBase> GetByStatus(TaskExecutionStatus status)
    {
        var scope = GetCurrentScope();
        if (scope is null) return [];
        return scope.GetAll<AgentBase>().Where(a => a.Status == status).ToList();
    }

    /// <summary>
    /// 暂停当前会话的指定主 Agent 的所有子 Agent
    /// </summary>
    public static void PauseAll(ObjectId mainAgentId)
    {
        foreach (var agent in GetSubAgents(mainAgentId))
            agent.Pause();
    }

    /// <summary>
    /// 恢复当前会话的指定主 Agent 的所有子 Agent
    /// </summary>
    public static void ResumeAll(ObjectId mainAgentId)
    {
        foreach (var agent in GetSubAgents(mainAgentId))
            agent.Resume();
    }

    /// <summary>
    /// 取消当前会话的指定主 Agent 的所有子 Agent
    /// </summary>
    public static void CancelAll(ObjectId mainAgentId)
    {
        foreach (var agent in GetSubAgents(mainAgentId))
            agent.Cancel();
    }

    /// <summary>
    /// 暂停所有会话的所有 Agent — 跨会话操作, 替代 AgentRegistry.PauseGlobal
    /// </summary>
    public static void PauseGlobal()
    {
        foreach (var scope in SessionRouter.GetAllScopes())
            foreach (var agent in scope.GetAll<AgentBase>())
                agent.Pause();
    }

    /// <summary>
    /// 恢复所有会话的所有 Agent — 跨会话操作, 替代 AgentRegistry.ResumeGlobal
    /// </summary>
    public static void ResumeGlobal()
    {
        foreach (var scope in SessionRouter.GetAllScopes())
            foreach (var agent in scope.GetAll<AgentBase>())
                agent.Resume();
    }
}
