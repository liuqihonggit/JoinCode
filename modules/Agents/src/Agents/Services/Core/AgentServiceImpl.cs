using JoinCode.Abstractions.Attributes;

namespace Core.Agents;

/// <summary>
/// AgentServiceImpl 可选依赖聚合 — 4 个可选服务封装为单个参数
/// </summary>
[Register]
public sealed record AgentServiceDependencies(
    JoinCode.Abstractions.Interfaces.IAgentTranscriptService? TranscriptService = null,
    IAgentMessageBroker? MessageBroker = null,
    SwarmPermissionCallbackService? PermissionCallbackService = null,
    JoinCode.Abstractions.Interfaces.IAgentMcpServerManager? McpServerManager = null);

[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentService))]
public sealed partial class AgentServiceImpl : JoinCode.Abstractions.Interfaces.IAgentService, IDisposable
{
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider _definitionProvider;
    private readonly JoinCode.Abstractions.Interfaces.IAgentTranscriptService? _transcriptService;
    private readonly IAgentMessageBroker? _messageBroker;
    private readonly SwarmPermissionCallbackService? _permissionCallbackService;
    private readonly JoinCode.Abstractions.Interfaces.IAgentMcpServerManager? _mcpServerManager;
    private readonly JoinCode.Abstractions.Interfaces.IAgentNotificationQueue? _notificationQueue;
    [Inject] private readonly ILogger<AgentServiceImpl>? _logger;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly IClockService _clock;
    private readonly Infrastructure.Pipeline.MiddlewarePipeline<AgentSpawnContext> _spawnPipeline;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JoinCode.Abstractions.Interfaces.AgentResult>> _completionSources;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _backgroundCts;
    private readonly ConcurrentDictionary<string, DateTime> _agentStartTimes;
    private readonly ConcurrentDictionary<string, ProgressTracker> _progressTrackers;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    public event EventHandler<JoinCode.Abstractions.Interfaces.AgentCompletedEventArgs>? AgentCompleted;

    public AgentServiceImpl(
        IAgentLifecycleManager lifecycleManager,
        JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider definitionProvider,
        Infrastructure.Pipeline.MiddlewarePipeline<AgentSpawnContext> spawnPipeline,
        AgentServiceDependencies? deps = null,
        JoinCode.Abstractions.Interfaces.IAgentNotificationQueue? notificationQueue = null,
        ILogger<AgentServiceImpl>? logger = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null)
    {
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _definitionProvider = definitionProvider ?? throw new ArgumentNullException(nameof(definitionProvider));
        _spawnPipeline = spawnPipeline ?? throw new ArgumentNullException(nameof(spawnPipeline));
        _transcriptService = deps?.TranscriptService;
        _messageBroker = deps?.MessageBroker;
        _permissionCallbackService = deps?.PermissionCallbackService;
        _mcpServerManager = deps?.McpServerManager;
        _notificationQueue = notificationQueue;
        _logger = logger;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
        _completionSources = new ConcurrentDictionary<string, TaskCompletionSource<JoinCode.Abstractions.Interfaces.AgentResult>>();
        _backgroundCts = new ConcurrentDictionary<string, CancellationTokenSource>();
        _agentStartTimes = new ConcurrentDictionary<string, DateTime>();
        _progressTrackers = new ConcurrentDictionary<string, ProgressTracker>();
    }

    /// <summary>
    /// 子智能体初始化结果 — SpawnAgentAsync / RunAgentStreamAsync 共享
    /// </summary>
    private sealed record SubAgentInitResult(ISubAgent SubAgent, string SystemPrompt, JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? Definition);

    /// <summary>
    /// 共享初始化流程 — 通过中间件管道执行: Definition → Prompt → Context → Hook → Mcp → Metadata → Transcript
    /// </summary>
    private async Task<SubAgentInitResult> InitializeSubAgentAsync(JoinCode.Abstractions.Interfaces.AgentSpawnOptions options, CancellationToken cancellationToken)
    {
        var context = new AgentSpawnContext
        {
            Options = options,
            CancellationToken = cancellationToken
        };

        await _spawnPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        if (context.SubAgent is null)
            throw new InvalidOperationException("中间件管道未创建 SubAgent");

        StartWorkerPermissionResponseRouting(context.SubAgent.Id);
        _progressTrackers[context.SubAgent.Id] = context.ProgressTracker;

        return new SubAgentInitResult(context.SubAgent, context.SystemPrompt, context.Definition);
    }

    public async Task<JoinCode.Abstractions.Interfaces.AgentInfo> SpawnAgentAsync(JoinCode.Abstractions.Interfaces.AgentSpawnOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var init = await InitializeSubAgentAsync(options, cancellationToken).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<JoinCode.Abstractions.Interfaces.AgentResult>();
        _completionSources[init.SubAgent.Id] = tcs;
        _agentStartTimes[init.SubAgent.Id] = _clock.GetUtcNow();

        var runInBackground = options.RunInBackground || (init.Definition?.IsBackground ?? false);

        if (runInBackground)
        {
            var backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _backgroundCts[init.SubAgent.Id] = backgroundCts;

            _ = RunBackgroundAgentAsync(init.SubAgent, tcs, backgroundCts.Token).WaitAsync(TimeSpan.FromSeconds(10), backgroundCts.Token).ConfigureAwait(false);

            _logger?.LogInformation("[AgentServiceImpl] 后台代理 {AgentId} 已启动 (fire-and-forget)", init.SubAgent.Id);

            return MapToAgentInfo(init.SubAgent);
        }

        var result = await _lifecycleManager.ExecuteAsync(init.SubAgent, cancellationToken).ConfigureAwait(false);

        var agentResult = MapToResult(result);
        tcs.SetResult(agentResult);

        FireAgentCompleted(init.SubAgent, agentResult);

        return MapToAgentInfo(init.SubAgent, result);
    }

    public async IAsyncEnumerable<AgentStreamChunk> RunAgentStreamAsync(
        AgentSpawnOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var init = await InitializeSubAgentAsync(options, cancellationToken).ConfigureAwait(false);

        _agentStartTimes[init.SubAgent.Id] = _clock.GetUtcNow();

        // 流式消费 SubAgent 的输出 — 对齐 TS for await (const message of runAgent(...))
        var responseBuilder = new StringBuilder();
        long? executionTimeMs = null;
        var succeeded = true;
        string? errorMessage = null;

        await foreach (var chunk in init.SubAgent.ExecuteStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            // 收集内容用于最终结果
            if (chunk.Type == AgentStreamChunkType.Content && chunk.Content is not null)
            {
                responseBuilder.Append(chunk.Content);
            }
            else if (chunk.Type == AgentStreamChunkType.Complete)
            {
                executionTimeMs = chunk.ExecutionTimeMs;
                // Complete 块的 Content 是最终输出，追加到响应
                if (chunk.Content is not null)
                {
                    responseBuilder.Append(chunk.Content);
                }
            }
            else if (chunk.Type == AgentStreamChunkType.Error)
            {
                succeeded = false;
                errorMessage = chunk.Content;
            }

            yield return chunk;
        }

        // 设置完成源
        var agentResult = new JoinCode.Abstractions.Interfaces.AgentResult
        {
            AgentId = init.SubAgent.Id,
            Success = succeeded,
            Output = succeeded ? responseBuilder.ToString() : string.Empty,
            Error = errorMessage
        };

        if (_completionSources.TryRemove(init.SubAgent.Id, out var tcs))
        {
            tcs.SetResult(agentResult);
        }

        FireAgentCompleted(init.SubAgent, agentResult);
    }

    public async Task<JoinCode.Abstractions.Interfaces.AgentResult> WaitForAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (_completionSources.TryGetValue(agentId, out var tcs))
        {
#pragma warning disable VSTHRD003
            return await tcs.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }

        var result = await _lifecycleManager.GetResultAsync(agentId, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return new JoinCode.Abstractions.Interfaces.AgentResult
            {
                AgentId = agentId,
                Success = false,
                Output = string.Empty,
                Error = "Agent result not found"
            };
        }

        return MapToResult(result);
    }

    public async Task<JoinCode.Abstractions.Interfaces.AgentInfo?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var subAgent = await _lifecycleManager.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);

        return subAgent is null ? null : MapToAgentInfo(subAgent);
    }

    public async Task<bool> StopAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (_backgroundCts.TryRemove(agentId, out var backgroundCts))
        {
            await backgroundCts.CancelAsync().ConfigureAwait(false);
            backgroundCts.Dispose();
        }

        await CleanupMcpServersIfNeededAsync(agentId, cancellationToken).ConfigureAwait(false);

        return await _lifecycleManager.CancelAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    public Task<JoinCode.Abstractions.Interfaces.AgentProgress?> GetAgentProgressAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (_progressTrackers.TryGetValue(agentId, out var tracker))
            return Task.FromResult<JoinCode.Abstractions.Interfaces.AgentProgress?>(tracker.ToProgress());

        return Task.FromResult<JoinCode.Abstractions.Interfaces.AgentProgress?>(null);
    }

    public async Task<List<JoinCode.Abstractions.Interfaces.AgentTypeInfo>> GetAvailableAgentTypesAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _definitionProvider.GetAgentDefinitionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return definitions.Select(d => new JoinCode.Abstractions.Interfaces.AgentTypeInfo
        {
            Name = d.AgentType,
            Description = d.Description ?? d.WhenToUse,
            AvailableTools = d.Tools
        }).ToList();
    }

    public async Task<JoinCode.Abstractions.Interfaces.AgentInfo> ResumeAgentAsync(JoinCode.Abstractions.Interfaces.AgentResumeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_transcriptService is null)
            throw new InvalidOperationException("IAgentTranscriptService 未注册，无法恢复代理");

        var sessionId = options.SessionId ?? "default";

        var metadata = await _transcriptService.LoadMetadataAsync(sessionId, options.AgentId, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
            throw new InvalidOperationException($"代理元数据不存在: {options.AgentId}");

        var transcript = await _transcriptService.LoadTranscriptAsync(sessionId, options.AgentId, cancellationToken).ConfigureAwait(false);
        if (transcript.Count == 0)
            throw new InvalidOperationException($"代理对话记录为空: {options.AgentId}");

        var chatHistory = TranscriptConverter.ToMessageListWithNewPrompt(transcript, options.NewPrompt);

        var definition = !string.IsNullOrWhiteSpace(metadata.AgentType)
            ? await _definitionProvider.GetAgentDefinitionAsync(metadata.AgentType, cancellationToken: cancellationToken).ConfigureAwait(false)
            : null;

        var subOptions = new SubAgentOptions
        {
            AgentType = metadata.AgentType,
            AdditionalInstructions = options.NewPrompt,
            ModelName = metadata.ModelName ?? definition?.ModelName,
            Temperature = definition?.Temperature ?? 0.7f,
            DisplayName = metadata.Description ?? "Resumed Agent",
            SystemPrompt = null,
            AllowedTools = definition?.Tools,
            DeniedTools = definition?.DisallowedTools,
            InitialMessageList = chatHistory,
            PreloadSkills = definition?.Skills,
            PermissionMode = definition?.PermissionMode,
        };

        var description = $"Resume: {metadata.Description}";
        var subAgent = await _lifecycleManager.SpawnSubAgentAsync(description, subOptions, cancellationToken).ConfigureAwait(false);

        if (subAgent.Context is not null)
        {
            subAgent.Context.ParentAgentId = _subAgentContextAccessor.Current?.AgentId;
            subAgent.Context.SessionId = sessionId;
        }

        await AppendTranscriptEntryAsync(subAgent.Id, "system", $"[RESUME from {options.AgentId}]", cancellationToken).ConfigureAwait(false);
        await AppendTranscriptEntryAsync(subAgent.Id, "user", options.NewPrompt, cancellationToken).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<JoinCode.Abstractions.Interfaces.AgentResult>();
        _completionSources[subAgent.Id] = tcs;
        _agentStartTimes[subAgent.Id] = _clock.GetUtcNow();

        if (options.RunInBackground)
        {
            var backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _backgroundCts[subAgent.Id] = backgroundCts;

            _ = RunBackgroundAgentAsync(subAgent, tcs, backgroundCts.Token).WaitAsync(TimeSpan.FromSeconds(10), backgroundCts.Token).ConfigureAwait(false);

            _logger?.LogInformation("[AgentServiceImpl] 恢复的代理 {NewAgentId} 已启动 (从 {OriginalAgentId} 恢复)", subAgent.Id, options.AgentId);

            return MapToAgentInfo(subAgent);
        }

        var result = await _lifecycleManager.ExecuteAsync(subAgent, cancellationToken).ConfigureAwait(false);

        var agentResult = MapToResult(result);
        tcs.SetResult(agentResult);

        FireAgentCompleted(subAgent, agentResult);

        return MapToAgentInfo(subAgent, result);
    }

    public async Task<bool> SendMessageToAgentAsync(string agentId, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (_messageBroker is null)
        {
            _logger?.LogWarning("[AgentServiceImpl] IAgentMessageBroker 未注册，无法发送消息");
            return false;
        }

        var agentMessage = new CoordinatorAgentMessage
        {
            FromAgentId = "parent",
            ToAgentId = agentId,
            MessageType = "user_message",
            Content = message
        };

        var sent = await _messageBroker.SendMessageAsync(agentId, agentMessage, cancellationToken).ConfigureAwait(false);

        if (sent)
        {
            _logger?.LogInformation("[AgentServiceImpl] 消息已发送给代理 {AgentId}", agentId);
            await AppendTranscriptEntryAsync(agentId, "user", $"[MESSAGE] {message}", cancellationToken).ConfigureAwait(false);
        }

        return sent;
    }

    /// <summary>
    /// 向运行中的代理发送结构化消息 — 对齐 TS SendMessageTool 结构化消息路由
    /// 将结构化消息数据包装为 AgentMessage，通过 AgentMessageBroker 路由
    /// </summary>
    public async Task<bool> SendStructuredMessageAsync(string agentId, StructuredMessageData structuredData, string rawMessage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (_messageBroker is null)
        {
            _logger?.LogWarning("[AgentServiceImpl] IAgentMessageBroker 未注册，无法发送结构化消息");
            return false;
        }

        var messageType = structuredData.Type.ToValue();
        var agentMessage = new CoordinatorAgentMessage
        {
            FromAgentId = "parent",
            ToAgentId = agentId,
            MessageType = messageType,
            Content = rawMessage,
            StructuredType = structuredData.Type,
            RequestId = structuredData.RequestId,
            Payload = structuredData.Payload
        };

        var sent = await _messageBroker.SendMessageAsync(agentId, agentMessage, cancellationToken).ConfigureAwait(false);

        if (sent)
        {
            _logger?.LogInformation("[AgentServiceImpl] 结构化消息({Type})已发送给代理 {AgentId}", messageType, agentId);
            await AppendTranscriptEntryAsync(agentId, "user", $"[{messageType.ToUpperInvariant()}] {rawMessage}", cancellationToken).ConfigureAwait(false);
        }

        return sent;
    }

    public async Task<IReadOnlyList<JoinCode.Abstractions.Interfaces.AgentMessageInfo>> GetAgentMessagesAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (_messageBroker is null)
        {
            _logger?.LogWarning("[AgentServiceImpl] IAgentMessageBroker 未注册，无法获取消息");
            return [];
        }

        var messages = new List<JoinCode.Abstractions.Interfaces.AgentMessageInfo>();

        await foreach (var msg in _messageBroker.ReadMessagesAsync(agentId, cancellationToken).ConfigureAwait(false))
        {
            messages.Add(new JoinCode.Abstractions.Interfaces.AgentMessageInfo
            {
                FromAgentId = msg.FromAgentId,
                MessageType = msg.MessageType,
                Content = msg.Content,
                Timestamp = msg.Timestamp
            });
        }

        return messages;
    }

    private void StartWorkerPermissionResponseRouting(string agentId)
    {
        if (_messageBroker is null || _permissionCallbackService is null) return;

        try
        {
            _ = Task.Run(async () =>
            {
                await foreach (var message in _messageBroker.ReadMessagesAsync(agentId).ConfigureAwait(false))
                {
                    if (message.MessageType == SwarmPermissionMessageType.PermissionResponse.ToValue())
                    {
                        await _permissionCallbackService.ProcessIncomingResponseMessageAsync(message).ConfigureAwait(false);
                    }
                }
            }).ConfigureAwait(false);

            _logger?.LogDebug("[AgentServiceImpl] Worker 权限响应路由已启动: AgentId={AgentId}", agentId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AgentServiceImpl] 启动 Worker 权限响应路由失败: AgentId={AgentId}", agentId);
        }
    }

    private async Task RunBackgroundAgentAsync(ISubAgent subAgent, TaskCompletionSource<JoinCode.Abstractions.Interfaces.AgentResult> tcs, CancellationToken cancellationToken)
    {
        using var scope = subAgent.Context?.EnterScopeWithCwd(subAgent.Options.WorktreePath);
        try
        {
            var result = await _lifecycleManager.ExecuteAsync(subAgent, cancellationToken).ConfigureAwait(false);

            var agentResult = MapToResult(result);
            tcs.SetResult(agentResult);

            FireAgentCompleted(subAgent, agentResult);
        }
        catch (OperationCanceledException)
        {
            var agentResult = new JoinCode.Abstractions.Interfaces.AgentResult
            {
                AgentId = subAgent.Id,
                Success = false,
                Output = string.Empty,
                Error = "Agent was cancelled"
            };
            tcs.SetResult(agentResult);

            FireAgentCompleted(subAgent, agentResult);
        }
        catch (Exception ex)
        {
            var agentResult = new JoinCode.Abstractions.Interfaces.AgentResult
            {
                AgentId = subAgent.Id,
                Success = false,
                Output = string.Empty,
                Error = ex.Message
            };
            tcs.SetResult(agentResult);

            FireAgentCompleted(subAgent, agentResult);
        }
        finally
        {
            _backgroundCts.TryRemove(subAgent.Id, out var cts);
            cts?.Dispose();
        }
    }

    private void FireAgentCompleted(ISubAgent subAgent, JoinCode.Abstractions.Interfaces.AgentResult result)
    {
        try
        {
            var status = result.Success ? AgentStatus.Completed : AgentStatus.Failed;

            if (_progressTrackers.TryGetValue(subAgent.Id, out var tracker))
            {
                if (subAgent.Context is not null)
                    tracker.RecordTokenUsage(subAgent.Context.TokenUsage.TotalTokens);
            }

            var durationMs = _agentStartTimes.TryRemove(subAgent.Id, out var startTime)
                ? (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds
                : (long?)null;

            var toolUseCount = _progressTrackers.TryGetValue(subAgent.Id, out var t) ? t.ToolUseCount : (int?)null;
            var tokenCount = subAgent.Context?.TokenUsage.TotalTokens;

            AgentCompleted?.Invoke(this, new JoinCode.Abstractions.Interfaces.AgentCompletedEventArgs
            {
                AgentId = subAgent.Id,
                Status = status,
                Description = subAgent.Task,
                Output = result.Output,
                Error = result.Error,
                ExecutionTimeMs = durationMs,
                AgentType = subAgent.Options.AgentType,
                ToolUseId = null,
                WorktreePath = subAgent.Options.WorktreePath,
                WorktreeBranch = subAgent.Options.WorktreeBranch,
                ToolUseCount = toolUseCount,
                TokenCount = tokenCount
            });

            var notification = new JoinCode.Abstractions.Interfaces.AgentTaskNotification
            {
                TaskId = subAgent.Id,
                Status = status.ToValue(),
                Description = subAgent.Task,
                ToolUseId = null,
                Output = result.Success ? result.Output : null,
                Error = result.Success ? null : result.Error,
                ExecutionTimeMs = durationMs,
                AgentType = subAgent.Options.AgentType,
                ToolUseCount = toolUseCount,
                TokenCount = tokenCount,
                WorktreePath = subAgent.Options.WorktreePath,
                WorktreeBranch = subAgent.Options.WorktreeBranch
            };

            _notificationQueue?.Enqueue(subAgent.Context?.ParentAgentId, notification.ToXml());

            _ = PersistCompletionAsync(subAgent, result, status, _disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AgentServiceImpl] 触发AgentCompleted事件失败: {AgentId}", subAgent.Id);
        }
    }

    private async Task PersistCompletionAsync(ISubAgent subAgent, JoinCode.Abstractions.Interfaces.AgentResult result, AgentStatus status, CancellationToken cancellationToken)
    {
        if (_transcriptService is null) return;

        try
        {
            var role = result.Success ? "assistant" : "error";
            var content = result.Success ? result.Output : $"ERROR: {result.Error}";
            await AppendTranscriptEntryAsync(subAgent.Id, role, content, cancellationToken).ConfigureAwait(false);

            var durationMs = _agentStartTimes.TryRemove(subAgent.Id, out var startTime)
                ? (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds
                : (long?)null;

            await _transcriptService.SaveMetadataAsync("default", new JoinCode.Abstractions.Interfaces.AgentMetadata
            {
                AgentId = subAgent.Id,
                AgentType = subAgent.Options.AgentType,
                Description = subAgent.Task,
                WorktreePath = subAgent.Options.WorktreePath,
                ModelName = subAgent.Options.ModelName,
                CompletedAt = _clock.GetUtcNow(),
                Status = status.ToString(),
                ErrorMessage = result.Success ? null : result.Error,
                DurationMs = durationMs
            }, cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AgentServiceImpl] 持久化代理完成记录失败: {AgentId}", subAgent.Id);
        }
    }

    private async Task CleanupMcpServersIfNeededAsync(string agentId, CancellationToken cancellationToken)
    {
        if (_mcpServerManager is null) return;

        try
        {
            await _mcpServerManager.CleanupAgentMcpServersAsync(agentId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AgentServiceImpl] Agent {AgentId} MCP 服务器清理失败", agentId);
        }
    }

    private async Task AppendTranscriptEntryAsync(string agentId, string role, string content, CancellationToken cancellationToken = default)
    {
        if (_transcriptService is null) return;

        try
        {
            await _transcriptService.AppendEntryAsync("default", agentId, new TranscriptEntry
            {
                SessionId = "default",
                Role = role,
                Content = content,
                Timestamp = _clock.GetUtcNow(),
                AgentId = agentId,
                IsSidechain = true
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AgentServiceImpl] 写入代理Transcript失败: {AgentId}", agentId);
        }
    }

    private static JoinCode.Abstractions.Interfaces.AgentInfo MapToAgentInfo(ISubAgent subAgent, SubAgentResult? result = null)
    {
        return new JoinCode.Abstractions.Interfaces.AgentInfo
        {
            Id = subAgent.Id,
            Description = subAgent.Task,
            AgentType = subAgent.Options.AgentType,
            Status = subAgent.State.ToAgentStatus(),
            StartedAt = subAgent.StartedAt,
            CompletedAt = subAgent.CompletedAt,
            Output = result?.Output
        };
    }

    private static JoinCode.Abstractions.Interfaces.AgentResult MapToResult(SubAgentResult result)
    {
        return new JoinCode.Abstractions.Interfaces.AgentResult
        {
            AgentId = result.AgentId,
            Success = result.IsSuccess,
            Output = result.Output,
            Error = result.Error
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _disposeCts.Cancel();
        _disposeCts.Dispose();

        foreach (var kvp in _backgroundCts)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _backgroundCts.Clear();
    }
}
