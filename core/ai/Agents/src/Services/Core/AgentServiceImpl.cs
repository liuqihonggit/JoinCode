using JoinCode.Abstractions.Attributes;

namespace Core.Agents;

/// <summary>
/// AgentServiceImpl 可选依赖聚合 — 4 个可选服务封装为单个参数
/// </summary>
[Register(typeof(AgentServiceDependencies), ServiceLifetime.Singleton)]
public sealed record AgentServiceDependencies(
    JoinCode.Abstractions.Interfaces.IAgentTranscriptService? TranscriptService = null,
    IMailbox? MessageBroker = null,
    SwarmPermissionCallbackService? PermissionCallbackService = null,
    JoinCode.Abstractions.Interfaces.IAgentMcpServerManager? McpServerManager = null,
    JoinCode.Abstractions.Interfaces.IAgentInputForwardQueue? InputForwardQueue = null,
    JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager? OutputChannelManager = null,
    JoinCode.Abstractions.Interfaces.IAgentWorktreeManager? WorktreeManager = null);

[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentService), ServiceLifetime.Singleton)]
public sealed partial class AgentServiceImpl : ServiceEntity, JoinCode.Abstractions.Interfaces.IAgentService, IDisposable
{

    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider _definitionProvider;
    private readonly JoinCode.Abstractions.Interfaces.IAgentRoleRegistry _roleRegistry;
    private readonly JoinCode.Abstractions.Interfaces.IAgentTranscriptService? _transcriptService;
    private readonly IMailbox? _messageBroker;
    private readonly JoinCode.Abstractions.Interfaces.IAgentInputForwardQueue? _inputForwardQueue;
    private readonly JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager? _outputChannelManager;
    private readonly SwarmPermissionCallbackService? _permissionCallbackService;
    private readonly JoinCode.Abstractions.Interfaces.IAgentMcpServerManager? _mcpServerManager;
    private readonly JoinCode.Abstractions.Interfaces.IAgentWorktreeManager? _worktreeManager;
    private readonly JoinCode.Abstractions.Interfaces.IAgentNotificationQueue? _notificationQueue;
    private readonly ILogger<AgentServiceImpl>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IClockService _clock;
    private readonly Infrastructure.Pipeline.MiddlewarePipeline<UnifiedSpawnContext> _spawnPipeline;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JoinCode.Abstractions.Interfaces.AgentResult>> _completionSources;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _backgroundCts;
    private readonly ConcurrentDictionary<string, DateTime> _agentStartTimes;
    private readonly ConcurrentDictionary<string, ProgressTracker> _progressTrackers;
    private readonly Coordinator.Core.Messaging.AgentNameIndex _agentNameIndex = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    public event EventHandler<JoinCode.Abstractions.Interfaces.AgentCompletedEventArgs>? AgentCompleted;

    public AgentServiceImpl(
        IAgentLifecycleManager lifecycleManager,
        JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider definitionProvider,
        JoinCode.Abstractions.Interfaces.IAgentRoleRegistry roleRegistry,
        Infrastructure.Pipeline.MiddlewarePipeline<UnifiedSpawnContext> spawnPipeline,
        AgentServiceDependencies? deps = null,
        JoinCode.Abstractions.Interfaces.IAgentNotificationQueue? notificationQueue = null,
        ILogger<AgentServiceImpl>? logger = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null)
    {
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _definitionProvider = definitionProvider ?? throw new ArgumentNullException(nameof(definitionProvider));
        _roleRegistry = roleRegistry ?? throw new ArgumentNullException(nameof(roleRegistry));
        _spawnPipeline = spawnPipeline ?? throw new ArgumentNullException(nameof(spawnPipeline));
        _transcriptService = deps?.TranscriptService;
        _messageBroker = deps?.MessageBroker;
        _inputForwardQueue = deps?.InputForwardQueue;
        _outputChannelManager = deps?.OutputChannelManager;
        _permissionCallbackService = deps?.PermissionCallbackService;
        _mcpServerManager = deps?.McpServerManager;
        _worktreeManager = deps?.WorktreeManager;
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
    private sealed record SubAgentInitResult(IAgent SubAgent, string SystemPrompt, JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? Definition);

    /// <summary>
    /// 共享初始化流程 — 通过中间件管道执行: Definition → Prompt → Context → Hook → Mcp → Metadata → Transcript
    /// </summary>
    private async Task<SubAgentInitResult> InitializeSubAgentAsync(JoinCode.Abstractions.Interfaces.AgentSpawnOptions options, CancellationToken cancellationToken)
    {
        var context = new UnifiedSpawnContext
        {
            Task = options.Description,
            SpawnOptions = options,
            CancellationToken = cancellationToken,
        };

        await _spawnPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        if (context.Agent is null)
            throw new InvalidOperationException("[AGT008] 中间件管道未创建 Agent");

        StartWorkerPermissionResponseRouting(context.Agent.ObjectId.UniqueId);
        _progressTrackers[context.Agent.ObjectId.UniqueId] = context.ProgressTracker;

        return new SubAgentInitResult(context.Agent, context.SystemPrompt, context.Definition);
    }

    public async Task<JoinCode.Abstractions.Interfaces.AgentInfo> SpawnAgentAsync(JoinCode.Abstractions.Interfaces.AgentSpawnOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var init = await InitializeSubAgentAsync(options, cancellationToken).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<JoinCode.Abstractions.Interfaces.AgentResult>();
        _completionSources[init.SubAgent.ObjectId.UniqueId] = tcs;
        _agentStartTimes[init.SubAgent.ObjectId.UniqueId] = _clock.GetUtcNow();
        _inputForwardQueue?.Register(init.SubAgent.ObjectId.UniqueId);
        if (init.SubAgent is AgentBase baseAgent)
        {
            if (_inputForwardQueue is not null)
                baseAgent.InputForwardQueue = _inputForwardQueue;
            if (_outputChannelManager is not null)
                baseAgent.OutputChannelManager = _outputChannelManager;
        }
        RegisterAgentNameIndex(init.SubAgent);

        var runInBackground = options.RunInBackground || (init.Definition?.IsBackground ?? false);

        if (runInBackground)
        {
            var backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _backgroundCts[init.SubAgent.ObjectId.UniqueId] = backgroundCts;

            _ = RunBackgroundAgentAsync(init.SubAgent, tcs, backgroundCts.Token).WaitAsync(TimeSpan.FromSeconds(10), backgroundCts.Token).ConfigureAwait(false);

            _logger?.LogInformation("[AgentServiceImpl] 后台代理 {AgentId} 已启动 (fire-and-forget)", init.SubAgent.ObjectId.UniqueId);

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

        _agentStartTimes[init.SubAgent.ObjectId.UniqueId] = _clock.GetUtcNow();
        _inputForwardQueue?.Register(init.SubAgent.ObjectId.UniqueId);
        if (init.SubAgent is AgentBase streamBaseAgent)
        {
            if (_inputForwardQueue is not null)
                streamBaseAgent.InputForwardQueue = _inputForwardQueue;
            if (_outputChannelManager is not null)
                streamBaseAgent.OutputChannelManager = _outputChannelManager;
        }
        RegisterAgentNameIndex(init.SubAgent);

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
            AgentId = init.SubAgent.ObjectId.UniqueId,
            Success = succeeded,
            Output = succeeded ? responseBuilder.ToString() : string.Empty,
            Error = errorMessage
        };

        if (_completionSources.TryRemove(init.SubAgent.ObjectId.UniqueId, out var tcs))
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
        await CleanupWorktreeIfNeededAsync(agentId, cancellationToken).ConfigureAwait(false);

        return await _lifecycleManager.CancelAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 终止子代理时清理 worktree — 修复原先遗漏导致 worktree 静默残留（磁盘泄漏）。
    /// worktree 中有未提交变更时保留并记录 reason，无变更时移除。对齐 ForkExecutionMiddleware 的清理策略。
    /// </summary>
    private async Task CleanupWorktreeIfNeededAsync(string agentId, CancellationToken cancellationToken)
    {
        if (_worktreeManager is null) return;

        try
        {
            var cleanupDetail = await _worktreeManager.CleanupWorktreeAsync(agentId, cancellationToken).ConfigureAwait(false);
            if (cleanupDetail.Kept)
            {
                _logger?.LogInformation("Agent {AgentId} worktree kept: {Path} (reason: {Reason})",
                    agentId, cleanupDetail.WorktreePath, cleanupDetail.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Agent {AgentId} worktree cleanup failed", agentId);
        }
    }

    /// <summary>
    /// 获取所有正在运行的代理 — 委托到 IAgentLifecycleManager
    /// </summary>
    public Task<IEnumerable<RunningAgentInfo>> GetRunningAgentsAsync(CancellationToken cancellationToken = default)
        => _lifecycleManager.GetRunningAgentsAsync(cancellationToken);

    /// <summary>
    /// 按名称查找运行中子代理的 ID — O(1) 字典查找
    /// 匹配键: DisplayName → Name → Description → Id（均精确匹配，大小写不敏感）
    /// </summary>
    public Task<string?> FindAgentIdByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Task.FromResult(_agentNameIndex.Find(name));
    }

    /// <summary>
    /// 获取指定子代理的 worktree 隔离目录 — 供 GUI 右键直达资源管理器；
    /// 未启用 worktree 隔离或代理不存在返回 null
    /// </summary>
    public async Task<string?> GetAgentWorktreePathAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var agent = await _lifecycleManager.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        // WorktreePath 存于 AgentBase.Options（IAgent 接口不暴露），需向下转型
        return agent is AgentBase concrete ? concrete.Options.WorktreePath : null;
    }

    /// <summary>
    /// 注册子代理名称索引 — Spawn 时调用，建立 name→agentId 的多键映射
    /// </summary>
    private void RegisterAgentNameIndex(IAgent subAgent)
    {
        if (subAgent is not AgentBase baseAgent) return;
        _agentNameIndex.Register(subAgent.ObjectId.UniqueId, baseAgent.Name, baseAgent.Task, baseAgent.Options.DisplayName);
        _outputChannelManager?.Register(subAgent.ObjectId.UniqueId, baseAgent.Options.DisplayName ?? baseAgent.Name);
    }

    /// <summary>
    /// 注销子代理名称索引 — 完成时调用，仅移除属于该 agentId 的键（同名子代理不误删）
    /// </summary>
    private void UnregisterAgentNameIndex(IAgent subAgent)
    {
        if (subAgent is not AgentBase baseAgent) return;
        _agentNameIndex.Unregister(subAgent.ObjectId.UniqueId, baseAgent.Name, baseAgent.Task, baseAgent.Options.DisplayName);
        _outputChannelManager?.Unregister(subAgent.ObjectId.UniqueId);
    }

    public Task<JoinCode.Abstractions.Interfaces.AgentProgress?> GetAgentProgressAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (_progressTrackers.TryGetValue(agentId, out var tracker))
            return Task.FromResult<JoinCode.Abstractions.Interfaces.AgentProgress?>(tracker.ToProgress());

        return Task.FromResult<JoinCode.Abstractions.Interfaces.AgentProgress?>(null);
    }

    public Task<List<JoinCode.Abstractions.Interfaces.AgentTypeInfo>> GetAvailableAgentTypesAsync(CancellationToken cancellationToken = default)
    {
        var profiles = _roleRegistry.GetAllProfiles();

        var result = profiles.Select(p => new JoinCode.Abstractions.Interfaces.AgentTypeInfo
        {
            Name = p.DisplayId,
            Description = p.Description ?? p.WhenToUse,
            AvailableTools = p.AllowedTools?.ToList()
        }).ToList();

        return Task.FromResult(result);
    }

    public async Task<JoinCode.Abstractions.Interfaces.AgentInfo> ResumeAgentAsync(JoinCode.Abstractions.Interfaces.AgentResumeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_transcriptService is null)
            throw new InvalidOperationException("[AGT009] IAgentTranscriptService 未注册，无法恢复代理");

        var sessionId = options.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;

        var metadata = await _transcriptService.LoadMetadataAsync(sessionId, options.AgentId, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
            throw new InvalidOperationException($"[AGT015] 代理元数据不存在: {options.AgentId}");

        var transcript = await _transcriptService.LoadTranscriptAsync(sessionId, options.AgentId, cancellationToken).ConfigureAwait(false);
        if (!transcript.Any())
            throw new InvalidOperationException($"[AGT016] 代理对话记录为空: {options.AgentId}");

        var chatHistory = TranscriptConverter.ToMessageListWithNewPrompt(transcript.ToList(), options.NewPrompt);

        var profile = metadata.Variant.HasValue || metadata.Role != default
            ? _roleRegistry.GetProfile(metadata.Role, metadata.Variant)
            : null;

        var subOptions = new SubAgentOptions
        {
            Role = metadata.Role,
            Variant = metadata.Variant,
            AdditionalInstructions = options.NewPrompt,
            ModelName = metadata.ModelName ?? profile?.ModelName,
            Temperature = profile?.Temperature ?? 0.7f,
            DisplayName = metadata.Description ?? "Resumed Agent",
            SystemPrompt = null,
            AllowedTools = profile?.AllowedTools?.ToList(),
            DeniedTools = profile?.DisallowedTools?.ToList(),
            InitialMessageList = chatHistory,
            PreloadSkills = profile?.Skills?.ToList(),
            PermissionMode = profile?.PermissionMode,
        };

        var description = $"Resume: {metadata.Description}";
        var subAgent = await _lifecycleManager.SpawnSubAgentAsync(description, subOptions, cancellationToken).ConfigureAwait(false);

        var concreteAgent = (AgentBase)subAgent;
        if (concreteAgent.Context is not null)
        {
            concreteAgent.Context.ParentAgentId = _subAgentContextAccessor.Current?.AgentId;
            concreteAgent.Context.SessionId = sessionId;
        }

        await AppendTranscriptEntryAsync(subAgent.ObjectId.UniqueId, "system", $"[RESUME from {options.AgentId}]", cancellationToken).ConfigureAwait(false);
        await AppendTranscriptEntryAsync(subAgent.ObjectId.UniqueId, "user", options.NewPrompt, cancellationToken).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<JoinCode.Abstractions.Interfaces.AgentResult>();
        _completionSources[subAgent.ObjectId.UniqueId] = tcs;
        _agentStartTimes[subAgent.ObjectId.UniqueId] = _clock.GetUtcNow();

        if (options.RunInBackground)
        {
            var backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _backgroundCts[subAgent.ObjectId.UniqueId] = backgroundCts;

            _ = RunBackgroundAgentAsync(subAgent, tcs, backgroundCts.Token).WaitAsync(TimeSpan.FromSeconds(10), backgroundCts.Token).ConfigureAwait(false);

            _logger?.LogInformation("[AgentServiceImpl] 恢复的代理 {NewAgentId} 已启动 (从 {OriginalAgentId} 恢复)", subAgent.ObjectId.UniqueId, options.AgentId);

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
            _logger?.LogWarning("[AgentServiceImpl] IMailbox 未注册，无法发送消息");
            return false;
        }

        var agentMessage = new CoordinatorAgentMessage
        {
            FromAgentId = "parent",
            ToAgentId = agentId,
            MessageType = "user_message",
            Content = message
        };

        var sent = await _messageBroker.SendAsync(agentId, agentMessage, cancellationToken).ConfigureAwait(false);

        if (sent)
        {
            _logger?.LogInformation("[AgentServiceImpl] 消息已发送给代理 {AgentId}", agentId);
            await AppendTranscriptEntryAsync(agentId, "user", $"[MESSAGE] {message}", cancellationToken).ConfigureAwait(false);
        }

        return sent;
    }

    /// <summary>
    /// 将用户输入转发给运行中的子代理 — 用户在子代理运行期间追加的输入
    /// 消息入 IAgentInputForwardQueue，由子代理每轮 LLM 调用前主动 TryDrain 消费
    /// </summary>
    public async Task<bool> ForwardUserInputToAgentAsync(string agentId, string userInput, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);

        if (_inputForwardQueue is null)
        {
            _logger?.LogWarning("[AgentServiceImpl] IAgentInputForwardQueue 未注册，无法转发用户输入");
            return false;
        }

        await _inputForwardQueue.EnqueueAsync(agentId, userInput, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("[AgentServiceImpl] 用户输入已转发给子代理 {AgentId}", agentId);
        await AppendTranscriptEntryAsync(agentId, "user", $"[USER_FORWARD] {userInput}", cancellationToken).ConfigureAwait(false);

        return true;
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
            _logger?.LogWarning("[AgentServiceImpl] IMailbox 未注册，无法发送结构化消息");
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

        var sent = await _messageBroker.SendAsync(agentId, agentMessage, cancellationToken).ConfigureAwait(false);

        if (sent)
        {
            _logger?.LogInformation("[AgentServiceImpl] 结构化消息({Type})已发送给代理 {AgentId}", messageType, agentId);
            await AppendTranscriptEntryAsync(agentId, "user", $"[{messageType.ToUpperInvariant()}] {rawMessage}", cancellationToken).ConfigureAwait(false);
        }

        return sent;
    }

    public async Task<IEnumerable<JoinCode.Abstractions.Interfaces.AgentMessageInfo>> GetAgentMessagesAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (_messageBroker is null)
        {
            _logger?.LogWarning("[AgentServiceImpl] IMailbox 未注册，无法获取消息");
            return [];
        }

        var messages = new List<JoinCode.Abstractions.Interfaces.AgentMessageInfo>();

        await foreach (var msg in _messageBroker.ReceiveAsync(agentId, cancellationToken).ConfigureAwait(false))
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
                await foreach (var message in _messageBroker.ReceiveAsync(agentId).ConfigureAwait(false))
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

    private async Task RunBackgroundAgentAsync(IAgent subAgent, TaskCompletionSource<JoinCode.Abstractions.Interfaces.AgentResult> tcs, CancellationToken cancellationToken)
    {
        var concreteAgent = (AgentBase)subAgent;
        using var scope = concreteAgent.Context?.EnterScopeWithCwd(concreteAgent.Options.WorktreePath);
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
                AgentId = subAgent.ObjectId.UniqueId,
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
                AgentId = subAgent.ObjectId.UniqueId,
                Success = false,
                Output = string.Empty,
                Error = ex.Message
            };
            tcs.SetResult(agentResult);

            FireAgentCompleted(subAgent, agentResult);
        }
        finally
        {
            _backgroundCts.TryRemove(subAgent.ObjectId.UniqueId, out var cts);
            cts?.Dispose();
        }
    }

    private void FireAgentCompleted(IAgent subAgent, JoinCode.Abstractions.Interfaces.AgentResult result)
    {
        try
        {
            var concreteAgent = (AgentBase)subAgent;
            _inputForwardQueue?.Unregister(subAgent.ObjectId.UniqueId);
            UnregisterAgentNameIndex(subAgent);
            var status = result.Success ? AgentStatus.Completed : AgentStatus.Failed;

            if (_progressTrackers.TryGetValue(subAgent.ObjectId.UniqueId, out var tracker))
            {
                if (concreteAgent.Context is not null)
                    tracker.RecordTokenUsage(concreteAgent.Context.TokenUsage.TotalTokens);
            }

            var durationMs = _agentStartTimes.TryRemove(subAgent.ObjectId.UniqueId, out var startTime)
                ? (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds
                : (long?)null;

            var toolUseCount = _progressTrackers.TryGetValue(subAgent.ObjectId.UniqueId, out var t) ? t.ToolUseCount : (int?)null;
            var tokenCount = concreteAgent.Context?.TokenUsage.TotalTokens;

            AgentCompleted?.Invoke(this, new JoinCode.Abstractions.Interfaces.AgentCompletedEventArgs
            {
                AgentId = subAgent.ObjectId.UniqueId,
                Status = status,
                Description = subAgent.Task,
                Output = result.Output,
                Error = result.Error,
                ExecutionTimeMs = durationMs,
                Role = concreteAgent.Options.Role,
                Variant = concreteAgent.Options.Variant,
                ToolUseId = null,
                WorktreePath = concreteAgent.Options.WorktreePath,
                WorktreeBranch = concreteAgent.Options.WorktreeBranch,
                ToolUseCount = toolUseCount,
                TokenCount = tokenCount
            });

            var notification = new JoinCode.Abstractions.Interfaces.AgentTaskNotification
            {
                TaskId = subAgent.ObjectId.UniqueId,
                Status = status.ToValue(),
                Description = subAgent.Task,
                ToolUseId = null,
                Output = result.Success ? result.Output : null,
                Error = result.Success ? null : result.Error,
                ExecutionTimeMs = durationMs,
                Role = concreteAgent.Options.Role,
                Variant = concreteAgent.Options.Variant,
                ToolUseCount = toolUseCount,
                TokenCount = tokenCount,
                WorktreePath = concreteAgent.Options.WorktreePath,
                WorktreeBranch = concreteAgent.Options.WorktreeBranch
            };

            _notificationQueue?.Enqueue(concreteAgent.Context?.ParentAgentId, notification.ToXml());

            _ = PersistCompletionAsync(subAgent, result, status, _disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AgentServiceImpl] 触发AgentCompleted事件失败: {AgentId}", subAgent.ObjectId.UniqueId);
        }
    }

    private async Task PersistCompletionAsync(IAgent subAgent, JoinCode.Abstractions.Interfaces.AgentResult result, AgentStatus status, CancellationToken cancellationToken)
    {
        if (_transcriptService is null) return;

        try
        {
            var concreteAgent = (AgentBase)subAgent;
            var role = result.Success ? "assistant" : "error";
            var content = result.Success ? result.Output : $"ERROR: {result.Error}";
            await AppendTranscriptEntryAsync(subAgent.ObjectId.UniqueId, role, content, cancellationToken).ConfigureAwait(false);

            var durationMs = _agentStartTimes.TryRemove(subAgent.ObjectId.UniqueId, out var startTime)
                ? (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds
                : (long?)null;

            await _transcriptService.SaveMetadataAsync("default", new JoinCode.Abstractions.Interfaces.AgentMetadata
            {
                AgentId = subAgent.ObjectId.UniqueId,
                AgentType = concreteAgent.Options.Variant?.ToValue() ?? concreteAgent.Options.Role.ToValue(),
                Description = subAgent.Task,
                WorktreePath = concreteAgent.Options.WorktreePath,
                ModelName = concreteAgent.Options.ModelName,
                CompletedAt = _clock.GetUtcNow(),
                Status = status.ToString(),
                ErrorMessage = result.Success ? null : result.Error,
                DurationMs = durationMs
            }, cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AgentServiceImpl] 持久化代理完成记录失败: {AgentId}", subAgent.ObjectId.UniqueId);
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

    private static JoinCode.Abstractions.Interfaces.AgentInfo MapToAgentInfo(IAgent subAgent, SubAgentResult? result = null)
    {
        var concreteAgent = (AgentBase)subAgent;
        return new JoinCode.Abstractions.Interfaces.AgentInfo
        {
            Id = subAgent.ObjectId.UniqueId,
            Description = subAgent.Task,
            Role = concreteAgent.Options.Role,
            Variant = concreteAgent.Options.Variant,
            Status = concreteAgent.State.ToAgentStatus(),
            StartedAt = concreteAgent.StartedAt,
            CompletedAt = concreteAgent.CompletedAt,
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

    protected override void OnDispose()
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
