
namespace Core.Agents.Coordinator;

/// <summary>
/// 子Agent - 执行具体任务的工作单元
/// </summary>
public sealed class SubAgent : ISubAgent
{
    private readonly IQueryEngine _queryEngine;
    private readonly ILogger? _logger;
    private readonly IClockService _clock;
    private readonly List<string> _context;
    private readonly CancellationTokenSource _cts;
    private readonly SemaphoreSlim _pauseLock;
    private JoinCode.Abstractions.LLM.Chat.CacheSafeParams? _lastCacheSafeParams;

    public string Id { get; }
    public string Task { get; }
    public SubAgentOptions Options { get; }
    public SubAgentContext? Context { get; }
    public TaskExecutionStatus Status { get; set; }
    public TaskExecutionStatus State { get; set; }
    public DateTime CreatedAt { get; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; private set; }
    public CancellationTokenSource? CancellationTokenSource { get; set; }

    private bool _isPaused;
    private int _executionCount;

    public SubAgent(string id, string task, SubAgentOptions? options, IQueryEngine queryEngine,  ILogger? logger, IClockService? clock = null)
    {

        Id = id;
        Task = task;
        Options = options ?? new SubAgentOptions();
        _queryEngine = queryEngine;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _context = new List<string>();
        _cts = new CancellationTokenSource();
        _pauseLock = new SemaphoreSlim(1, 1);
        Status = TaskExecutionStatus.Pending;
        CreatedAt = _clock.GetUtcNow();
        _isPaused = false;
        _executionCount = 0;
        Context = new SubAgentContext
        {
            AgentId = id,
            AgentType = Options.AgentType ?? AgentTypeDefinition.Default.ToValue(),
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
    /// 添加上下文信息
    /// </summary>
    public void AddContext(string context)
    {
        _context.Add(context);
    }

    /// <summary>
    /// 执行Agent任务
    /// </summary>
    public async Task<SubAgentResult> ExecuteAsync(CancellationToken cancellationToken = default)
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
            _logger?.LogInformation(AgentCoordinatorConstants.LogMessages.SubAgentStartExecute, AgentCoordinatorConstants.LogMessages.SubAgentPrefix, Id, _executionCount);

            // 构建提示
            var prompt = BuildPrompt();

            MessageList chatHistory;
            if (Options.InitialMessageList is not null && Options.InitialMessageList.Count > 0)
            {
                chatHistory = Options.InitialMessageList;
            }
            else
            {
                chatHistory = new MessageList();
                var systemMessage = !string.IsNullOrWhiteSpace(Options.SystemPrompt)
                    ? Options.SystemPrompt
                    : string.Format(AgentCoordinatorConstants.SystemPrompts.SubAgentSystemMessage, Task);
                chatHistory.AddSystemMessage(systemMessage);
            }

            var responseBuilder = new StringBuilder();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var queryOptions = BuildChatOptions();

            await foreach (var chunk in _queryEngine.QueryAsync(prompt, chatHistory, queryOptions, linkedToken))
            {
                // 检查暂停状态 - 使用超时保护
                if (_isPaused)
                {
                    _logger?.LogInformation("[{AgentType} {AgentId}] 进入暂停等待状态", nameof(SubAgent), Id);
                    var pauseStart = _clock.GetUtcNow();

                    try
                    {
                        // 使用30秒超时，防止永久阻塞
                        await _pauseLock.WaitAsync(linkedToken).ConfigureAwait(false);
                        _pauseLock.Release();

                        var pauseDuration = _clock.GetUtcNow() - pauseStart;
                        _logger?.LogInformation("[{AgentType} {AgentId}] 暂停结束，等待时长 {PauseDurationMs}ms", nameof(SubAgent), Id, pauseDuration.TotalMilliseconds);
                    }
                    catch (TimeoutException)
                    {
                        _logger?.LogWarning("[{AgentType} {AgentId}] 暂停等待超时（30秒），自动恢复执行", nameof(SubAgent), Id);
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

            _logger?.LogInformation("[SubAgent {AgentId}] 任务执行完成，耗时{ElapsedMs}ms", Id, stopwatch.ElapsedMilliseconds);

            return new SubAgentResult
            {
                AgentId = Id,
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

            if (Context is not null)
            {
                Context.CompletedAt = CompletedAt;
                Context.Status = AgentStatus.Failed;
            }

            _logger?.LogError(ex, "[SubAgent {AgentId}] 任务执行失败", Id);

            return new SubAgentResult
            {
                AgentId = Id,
                IsSuccess = false,
                Output = string.Empty,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// 流式执行Agent任务 — 对齐 TS runAgent AsyncGenerator
    /// 将 _queryEngine.QueryAsync 的流式输出包装为 AgentStreamChunk 向上透传
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
            var systemMessage = !string.IsNullOrWhiteSpace(Options.SystemPrompt)
                ? Options.SystemPrompt
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
            // 检查暂停状态
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

            // 直接使用 AgentStreamChunkType（已与 AgentStreamChunkType 合并）
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
                AgentId = Id
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

        // yield 最终完成块
        yield return new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            Content = succeeded ? responseBuilder.ToString() : errorMessage,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            AgentId = Id
        };
    }

    /// <summary>
    /// 暂停任务
    /// </summary>
    public void Pause()
    {
        if (Status == TaskExecutionStatus.Running)
        {
            _isPaused = true;
            Status = TaskExecutionStatus.Paused;
            _logger?.LogInformation("[{AgentType} {AgentId}] 任务已暂停，等待恢复信号", nameof(SubAgent), Id);
        }
    }

    /// <summary>
    /// 恢复任务
    /// </summary>
    public void Resume()
    {
        if (Status == TaskExecutionStatus.Paused)
        {
            _isPaused = false;
            Status = TaskExecutionStatus.Running;
            _logger?.LogInformation("[{AgentType} {AgentId}] 任务已恢复，释放暂停锁", nameof(SubAgent), Id);
        }
    }

    /// <summary>
    /// 取消任务
    /// </summary>
    public void Cancel()
    {
        _cts.Cancel();
        Status = TaskExecutionStatus.Cancelled;
        _logger?.LogInformation("[SubAgent {AgentId}] 任务已取消", Id);
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
        _logger?.LogInformation("[SubAgent {AgentId}] 状态已重置", Id);
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

    public void Dispose()
    {
        _cts.Dispose();
        _pauseLock.Dispose();
    }
}
