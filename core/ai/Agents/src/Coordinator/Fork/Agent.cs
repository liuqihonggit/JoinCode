
namespace Core.Agents.Coordinator;

/// <summary>
/// 统一 Agent — 派生自 Entity，共享 ObjectId/CreatedAt/惰性释放
/// mainAgent 和 subAgent 共用此类
/// LLM对话循环内聚，外部只通过 IAgent 控制生命周期
/// </summary>
public sealed class Agent : Entity, IAgent
{
    private readonly IQueryEngine _queryEngine;
    private readonly ILogger? _logger;
    private readonly IClockService _clock;
    private readonly List<string> _context;
    private readonly CancellationTokenSource _cts;
#pragma warning disable JCC4005 // SemaphoreSlim 在 OnDispose() 中释放，分析器无法追踪间接调用路径
    private readonly SemaphoreSlim _pauseLock;
#pragma warning restore JCC4005
    private JoinCode.Abstractions.LLM.Chat.CacheSafeParams? _lastCacheSafeParams;

    // === 静态注册器 ===
    public static AgentRegistry Registry { get; } = new();

    // === 身份（ObjectId/UniqueId/CreatedAt 继承自 Entity）===
    public string Name { get; }
    public bool IsSubAgent { get; }
    public ObjectId? ParentObjectId { get; init; }
    public string? AgentType { get; }

    // === 任务 ===
    public string Task { get; }
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

    private bool _isPaused;
    private int _executionCount;

    public Agent(
        string task,
        SubAgentOptions? options,
        IQueryEngine queryEngine,
        ILogger? logger,
        IClockService? clock = null,
        string? name = null,
        bool isSubAgent = true,
        ObjectId? parentObjectId = default,
        string? agentType = null,
        string? systemPrompt = null,
        string? instruction = null,
        bool freshContext = false,
        int? tokenBudget = null,
        string? goalId = null,
        string? graphNodeId = null)
        : base(ObjectType.Agent)
    {
        Task = task;
        Name = name ?? UniqueId;
        IsSubAgent = isSubAgent;
        ParentObjectId = parentObjectId;
        AgentType = agentType;
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
        _pauseLock = new SemaphoreSlim(1, 1);
        Status = TaskExecutionStatus.Pending;
        _isPaused = false;
        _executionCount = 0;
        Context = new SubAgentContext
        {
            AgentId = UniqueId,
            AgentType = agentType ?? Options.AgentType ?? AgentTypeDefinition.Default.ToValue(),
            Task = task,
            AllowedTools = Options.AllowedTools,
            DeniedTools = Options.DeniedTools,
            SubagentName = Options.SubagentName,
            IsBuiltIn = Options.IsBuiltIn,
            DisplayName = Options.DisplayName,
            PermissionMode = Options.PermissionMode
        };

        Registry.Add(ObjectId, this);
    }

    /// <summary>
    /// 惰性释放 — 持久化服务确认消息全部写入后才调用
    /// </summary>
    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
        _cts.Dispose();
        _pauseLock.Dispose();
    }

    /// <summary>
    /// 生成唯一 Agent Id
    /// </summary>
    public static string GenerateId() => $"agent-{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// 添加上下文信息
    /// </summary>
    public void AddContext(string context)
    {
        _context.Add(context);
    }

    /// <summary>
    /// 执行Agent任务
    /// </summary>
    public async System.Threading.Tasks.Task<SubAgentResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var linkedToken = linkedCts.Token;

        StartedAt = _clock.GetUtcNow();
        Status = TaskExecutionStatus.Running;
        _executionCount++;

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

            var responseBuilder = new StringBuilder();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var queryOptions = BuildChatOptions();

            await foreach (var chunk in _queryEngine.QueryAsync(prompt, chatHistory, queryOptions, linkedToken))
            {
                if (_isPaused)
                {
                    _logger?.LogInformation("[{AgentType} {AgentId}] 进入暂停等待状态", nameof(Agent), UniqueId);
                    var pauseStart = _clock.GetUtcNow();

                    try
                    {
                        await _pauseLock.WaitAsync(linkedToken).ConfigureAwait(false);
                        _pauseLock.Release();

                        var pauseDuration = _clock.GetUtcNow() - pauseStart;
                        _logger?.LogInformation("[{AgentType} {AgentId}] 暂停结束，等待时长 {PauseDurationMs}ms", nameof(Agent), UniqueId, pauseDuration.TotalMilliseconds);
                    }
                    catch (TimeoutException)
                    {
                        _logger?.LogWarning("[{AgentType} {AgentId}] 暂停等待超时（30秒），自动恢复执行", nameof(Agent), UniqueId);
                        _isPaused = false;
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
    /// 流式执行Agent任务 — 对齐 TS runAgent AsyncGenerator
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var linkedToken = linkedCts.Token;

        StartedAt = _clock.GetUtcNow();
        Status = TaskExecutionStatus.Running;
        _executionCount++;

        if (Context is not null)
        {
            Context.StartedAt = StartedAt;
            Context.Status = AgentStatus.Running;
        }

        using var scope = Context?.EnterScopeWithCwd(Options.WorktreePath);

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
            if (_isPaused)
            {
                try
                {
                    await _pauseLock.WaitAsync(linkedToken).ConfigureAwait(false);
                    _pauseLock.Release();
                }
                catch (TimeoutException)
                {
                    _isPaused = false;
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

            yield return new AgentStreamChunk
            {
                Type = chunk.Type,
                Content = chunk.Content,
                ToolName = chunk.ToolName,
                ToolCallNumber = chunk.ToolCallNumber,
                ToolResult = chunk.ToolResult,
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
    /// 暂停LLM循环
    /// </summary>
    public void Pause()
    {
        if (Status == TaskExecutionStatus.Running)
        {
            _isPaused = true;
            Status = TaskExecutionStatus.Paused;
            _logger?.LogInformation("[{AgentType} {AgentId}] 任务已暂停，等待恢复信号", nameof(Agent), UniqueId);
        }
    }

    /// <summary>
    /// 恢复LLM循环
    /// </summary>
    public void Resume()
    {
        if (Status == TaskExecutionStatus.Paused)
        {
            _isPaused = false;
            Status = TaskExecutionStatus.Running;
            _logger?.LogInformation("[{AgentType} {AgentId}] 任务已恢复，释放暂停锁", nameof(Agent), UniqueId);
        }
    }

    /// <summary>
    /// 取消LLM循环
    /// </summary>
    public void Cancel()
    {
        _cts.Cancel();
        Status = TaskExecutionStatus.Cancelled;
        _logger?.LogInformation("[Agent {AgentId}] 任务已取消", UniqueId);
    }

    /// <summary>
    /// 重置Agent状态（用于重试）
    /// </summary>
    public void Reset()
    {
        _isPaused = false;
        Status = TaskExecutionStatus.Pending;
        StartedAt = null;
        CompletedAt = null;
        _logger?.LogInformation("[Agent {AgentId}] 状态已重置", UniqueId);
    }

    private string BuildPrompt()
    {
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

    private QueryOptions? BuildChatOptions()
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
            AllowedTools = Options.AllowedTools,
            DeniedTools = Options.DeniedTools,
            CacheSafeParams = Options.CacheSafeParams,
            ProgressTracker = Options.ProgressTracker,
            ContentReplacementState = Options.ContentReplacementState,
            SessionId = Options.SessionId,
            EffortLevel = effortLevel,
            ModelId = Options.ModelName,
        };
    }
}
