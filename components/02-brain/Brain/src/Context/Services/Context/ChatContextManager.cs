
using JoinCode.Abstractions.Interfaces;

namespace Core.Context;

/// <summary>
/// 默认上下文窗口解析器 — 当 DI 未注入 IContextWindowResolver 时使用
/// 返回固定默认值 200K（对齐 TS MODEL_CONTEXT_WINDOW_DEFAULT）
/// </summary>
internal sealed class DefaultContextWindowResolver : IContextWindowResolver
{
    public int ResolveCurrentContextWindow() => 200_000;
}

/// <summary>
/// ChatContextManager 可选依赖聚合
/// </summary>
public sealed record ChatContextOptions
{
    public ContextFoldExecutor? FoldExecutor { get; init; }
    public ContextFoldThresholds? Thresholds { get; init; }
    public IContextWindowResolver? ContextWindowResolver { get; init; }
    public ISessionMetaStore? MetaStore { get; init; }
    public SessionStats? SessionStats { get; init; }
    public string? SessionId { get; init; }
    public ITelemetryService? TelemetryService { get; init; }
}

[Register(typeof(IChatContextManager))]
public partial class ChatContextManager : IChatContextManager, IAsyncDisposable
{
    private readonly IStateService _stateService;
    [Inject] private readonly ILogger<ChatContextManager> _logger;
    private readonly SemaphoreSlim _lock;
    private readonly ContextFoldExecutor? _foldExecutor;
    private readonly ContextFoldThresholds _thresholds;
    private readonly IContextWindowResolver _contextWindowResolver;
    private readonly ISessionMetaStore? _metaStore;
    private readonly SessionStats? _sessionStats;
    private readonly string _sessionId;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 当前会话标识
    /// </summary>
    public string SessionId => _sessionId;

    private string _staticSystemPrompt = string.Empty;
    private readonly List<string> _dynamicSystemMessages = [];
    private readonly List<ToolSpec> _currentToolSpecs = [];
    private readonly AppendOnlyLog _conversationLog = new();
    private readonly CacheBreakDetector _cacheBreakDetector = new();
    private readonly DiscoveredToolSet _discoveredTools = new();
    private readonly List<DeferredToolInfo> _deferredTools = [];
    private string _previousDynamicHash = string.Empty;

    public ChatContextManager(
        IStateService stateService,
        ILogger<ChatContextManager> logger,
        ChatContextOptions? options = null)
    {
        _stateService = stateService;
        _logger = logger;
        _lock = new SemaphoreSlim(1, 1);
        _foldExecutor = options?.FoldExecutor;
        _thresholds = options?.Thresholds ?? ContextFoldThresholds.Default;
        _contextWindowResolver = options?.ContextWindowResolver ?? new DefaultContextWindowResolver();
        _metaStore = options?.MetaStore;
        _sessionStats = options?.SessionStats;
        _sessionId = options?.SessionId ?? "default";
        _telemetryService = options?.TelemetryService;
    }

    /// <summary>
    /// 从持久化存储加载聊天上下文，恢复系统提示词和对话历史
    /// </summary>
    public async Task LoadContextAsync(CancellationToken cancellationToken = default)
    {
        await using var span = _telemetryService?.StartSpan("context.load", TelemetrySpanKind.Server);
        try
        {
            var (systemPrompt, chatHistory) = await _stateService.LoadStateAsync(cancellationToken).ConfigureAwait(false);

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _staticSystemPrompt = systemPrompt ?? string.Empty;
                _dynamicSystemMessages.Clear();
                _conversationLog.CompactInPlace([]);

                if (chatHistory is { Count: > 0 })
                {
                    foreach (var msg in chatHistory)
                    {
                        if (msg.Role == MessageRole.System)
                        {
                            if (string.IsNullOrWhiteSpace(_staticSystemPrompt))
                            {
                                _staticSystemPrompt = msg.Content ?? string.Empty;
                            }
                            continue;
                        }

                        _conversationLog.Append(new ApiMessage(msg.Role, msg.Content, msg.Metadata));
                    }
                }
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation("聊天上下文已加载，静态前缀长度: {Len}, 对话消息数: {Count}",
                _staticSystemPrompt.Length, _conversationLog.Count);

            span?.SetTag("context.message_count", _conversationLog.Count);
            span?.SetStatus(TelemetryStatusCode.Ok);

            if (_metaStore is not null && _sessionStats is not null)
            {
                var meta = await _metaStore.LoadAsync(_sessionId, cancellationToken).ConfigureAwait(false);
                if (meta is not null)
                {
                    _sessionStats.SeedCarryover(meta.CacheHitTokens, meta.CacheMissTokens, meta.TotalCostUsd);
                    _logger.LogInformation("会话统计已恢复，缓存命中: {Hit}, 未命中: {Miss}, 轮次: {Turns}",
                        meta.CacheHitTokens, meta.CacheMissTokens, meta.TurnCount);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载聊天上下文时出错");
            span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            span?.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// 添加用户消息到对话日志
    /// </summary>
    public async Task AddUserMessageAsync(string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _conversationLog.Append(new ApiMessage(MessageRole.User, content));
            _logger.LogDebug("已添加用户消息，当前对话数: {Count}", _conversationLog.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddCompactSummaryMessageAsync(string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _conversationLog.Append(new ApiMessage(MessageRole.User, content, new Dictionary<string, JsonElement>
            {
                ["isCompactSummary"] = JsonElementHelper.FromBoolean(true)
            }));
            _logger.LogDebug("已添加压缩摘要消息，当前对话数: {Count}", _conversationLog.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 添加助手消息到对话日志
    /// </summary>
    public async Task AddAssistantMessageAsync(string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _conversationLog.Append(new ApiMessage(MessageRole.Assistant, content));
            _logger.LogDebug("已添加助手消息，当前对话数: {Count}", _conversationLog.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 添加助手工具调用消息（含元数据）到对话日志
    /// </summary>
    public async Task AddAssistantToolCallMessageAsync(string? content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _conversationLog.Append(new ApiMessage(MessageRole.Assistant, content, metadata));
            _logger.LogDebug("已添加助手工具调用消息，当前对话数: {Count}", _conversationLog.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 添加工具结果消息到对话日志
    /// </summary>
    public async Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _conversationLog.Append(new ApiMessage(MessageRole.Tool, content, metadata));
            _logger.LogDebug("已添加工具结果消息，当前对话数: {Count}", _conversationLog.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 添加包含多模态内容的工具结果消息 — 对齐 TS BashTool image output
    /// </summary>
    public async Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, IReadOnlyList<ToolContent>? contentBlocks, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _conversationLog.Append(new ApiMessage(MessageRole.Tool, content, metadata) { ContentBlocks = contentBlocks });
            _logger.LogDebug("已添加工具结果消息(含多模态)，当前对话数: {Count}", _conversationLog.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 添加系统消息到对话日志
    /// </summary>
    public async Task AddSystemMessageAsync(string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _conversationLog.Append(new ApiMessage(MessageRole.System, content));
            _logger.LogDebug("已添加系统消息，当前对话数: {Count}", _conversationLog.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 添加动态系统消息，该消息独立于对话日志，会随前缀一起组装
    /// </summary>
    public async Task AddDynamicSystemMessageAsync(string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _dynamicSystemMessages.Add(content);
            _logger.LogDebug("已添加动态系统消息，当前动态消息数: {Count}", _dynamicSystemMessages.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 清空所有动态系统消息
    /// </summary>
    public async Task ClearDynamicSystemMessagesAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _dynamicSystemMessages.Clear();
            _logger.LogDebug("已清空动态系统消息");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 清空所有对话消息和动态系统消息，保留静态系统提示词
    /// </summary>
    public async Task ClearMessagesAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _conversationLog.CompactInPlace([]);
            _dynamicSystemMessages.Clear();

            _logger.LogInformation("聊天消息已清空，保留静态系统提示词");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 更新静态系统提示词
    /// </summary>
    public async Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _staticSystemPrompt = systemPrompt;
            _logger.LogInformation("静态系统提示词已更新，长度: {Len}", _staticSystemPrompt.Length);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 获取组装后的完整消息列表（静态系统提示词 + 动态系统消息 + 对话日志）
    /// </summary>
    public async Task<MessageList> GetMessageListAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messages = AssembleMessages();
            return new MessageList(messages);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 将当前聊天上下文持久化保存，包括系统提示词、对话历史和会话统计
    /// </summary>
    public async Task SaveContextAsync(CancellationToken cancellationToken = default)
    {
        await using var span = _telemetryService?.StartSpan("context.save", TelemetrySpanKind.Server);
        try
        {
            string staticPrefix;
            MessageList conversationSnapshot;

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                staticPrefix = _staticSystemPrompt;
                conversationSnapshot = new MessageList(_conversationLog.ToMessages());
            }
            finally
            {
                _lock.Release();
            }

            await _stateService.SaveStateAsync(staticPrefix, conversationSnapshot, cancellationToken).ConfigureAwait(false);

            if (_metaStore is not null && _sessionStats is not null)
            {
                var meta = _sessionStats.ToMeta();
                await _metaStore.SaveAsync(_sessionId, meta, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogDebug("聊天上下文已保存");

            span?.SetStatus(TelemetryStatusCode.Ok);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存聊天上下文时出错");
            span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            span?.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// 根据本次 token 用量决定是否需要折叠上下文
    /// </summary>
    public ContextFoldDecision DecideAfterUsage(TokenUsage usage, bool alreadyFoldedThisTurn = false)
    {
        return ContextFoldDecider.DecideAfterUsage(usage, _contextWindowResolver.ResolveCurrentContextWindow(), alreadyFoldedThisTurn, _thresholds);
    }

    /// <summary>
    /// 在发送请求前预判是否需要折叠，基于当前消息和工具规格估算 token 占用
    /// </summary>
    public PreflightDecision DecidePreflight(IReadOnlyList<ToolSpec> toolSpecs)
    {
        var messages = AssembleMessages();
        return ContextFoldDecider.DecidePreflight(messages, toolSpecs, _contextWindowResolver.ResolveCurrentContextWindow(), _thresholds);
    }

    /// <summary>
    /// 根据折叠决策执行上下文折叠操作（普通/激进/摘要退出）
    /// </summary>
    public async Task<ContextFoldResult> FoldIfNeededAsync(ContextFoldDecision decision, CancellationToken cancellationToken = default)
    {
        if (_foldExecutor == null)
        {
            return new ContextFoldResult
            {
                Folded = false,
                Decision = decision,
                OriginalMessageCount = _conversationLog.Count
            };
        }

        await using var foldSpan = _telemetryService?.StartSpan("context.fold", TelemetrySpanKind.Server);
        foldSpan?.SetTag("context.fold_decision", decision.ToString());

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return decision switch
            {
                ContextFoldDecision.FoldNormal => await _foldExecutor.FoldAsync(_conversationLog, _contextWindowResolver.ResolveCurrentContextWindow(), aggressive: false, _thresholds, cancellationToken).ConfigureAwait(false),
                ContextFoldDecision.FoldAggressive => await _foldExecutor.FoldAsync(_conversationLog, _contextWindowResolver.ResolveCurrentContextWindow(), aggressive: true, _thresholds, cancellationToken).ConfigureAwait(false),
                ContextFoldDecision.ExitWithSummary => _foldExecutor.TrimTrailingAndPrepareExit(_conversationLog),
                _ => new ContextFoldResult { Folded = false, Decision = decision, OriginalMessageCount = _conversationLog.Count }
            };
        }
        finally
        {
            _lock.Release();
            foldSpan?.SetStatus(TelemetryStatusCode.Ok);
            _telemetryService?.RecordCount("context.fold.count", new() { ["decision"] = decision.ToString() }, "count", "Context fold count");
        }
    }

    /// <summary>
    /// 获取当前上下文窗口的最大 token 数
    /// </summary>
    public int GetContextMaxTokens() => _contextWindowResolver.ResolveCurrentContextWindow();

    /// <summary>
    /// 撤回最后一轮对话（SP-3），移除最近的用户-助手消息对
    /// </summary>
    public async Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = _conversationLog.TrimLastTurn();
            _logger.LogInformation("撤回最后一轮对话 (SP-3)，移除 {Count} 条消息，剩余 {Remaining} 条",
                removed, _conversationLog.Count);

            _telemetryService?.RecordCount("context.rewind.count", new() { ["kind"] = "last_turn" }, "count", "Context rewind count");

            return RewindResult.Ok(RewindKind.TrimLastTurn, removed, _conversationLog.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 撤回到指定消息索引（SP-5），移除该索引之后的所有消息
    /// </summary>
    public async Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (messageIndex < 0 || messageIndex > _conversationLog.Count)
            {
                return RewindResult.Fail(
                    $"消息索引 {messageIndex} 超出范围 [0, {_conversationLog.Count}]");
            }

            var removed = _conversationLog.TruncateTo(messageIndex);
            _logger.LogInformation("撤回到消息索引 {Index} (SP-5)，移除 {Count} 条消息，剩余 {Remaining} 条",
                messageIndex, removed, _conversationLog.Count);

            return RewindResult.Ok(RewindKind.TruncateToIndex, removed, _conversationLog.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 撤回到会话初始状态（SP-0），清空所有对话消息和动态系统消息
    /// </summary>
    public async Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = _conversationLog.Count;
            _conversationLog.CompactInPlace([]);
            _dynamicSystemMessages.Clear();

            _logger.LogInformation("撤回到会话初始状态 (SP-0)，移除 {Count} 条消息，前缀保留", removed);

            return RewindResult.Ok(RewindKind.ClearHistory, removed, 0);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 更新当前可用的工具规格列表，同时识别并记录 MCP 延迟工具
    /// </summary>
    public async Task UpdateToolSpecsAsync(IReadOnlyList<ToolSpec> toolSpecs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolSpecs);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _currentToolSpecs.Clear();
            _currentToolSpecs.AddRange(toolSpecs);

            _deferredTools.Clear();
            foreach (var spec in toolSpecs)
            {
                var isMcp = spec.Name.Contains('.');
                if (isMcp)
                {
                    _deferredTools.Add(new DeferredToolInfo(spec.Name, spec.Description, spec.InputSchemaJson, isMcp: true));
                }
            }

            _logger.LogDebug("工具规格已更新，当前 {Count} 个工具，{DeferredCount} 个延迟工具",
                _currentToolSpecs.Count, _deferredTools.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 记录当前提示词前缀状态快照，用于后续缓存失效检测
    /// </summary>
    public async Task<PromptStateSnapshot> RecordPromptStateAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var prefix = new ImmutablePrefix(_staticSystemPrompt, _currentToolSpecs, []);
            var dynamicContent = string.Join("\n", _dynamicSystemMessages);
            var snapshot = _cacheBreakDetector.RecordPromptState(prefix, dynamicContent);

            var toolSpecsBytes = _currentToolSpecs.Sum(t =>
                System.Text.Encoding.UTF8.GetByteCount(t.Name) +
                (t.Description != null ? System.Text.Encoding.UTF8.GetByteCount(t.Description) : 0) +
                (t.InputSchemaJson != null ? System.Text.Encoding.UTF8.GetByteCount(t.InputSchemaJson) : 0));
            var systemBytes = System.Text.Encoding.UTF8.GetByteCount(_staticSystemPrompt);
            var estimatedTokens = ContextFoldDecider.EstimateTokenCount(
                [new ApiMessage(MessageRole.System, _staticSystemPrompt)],
                _currentToolSpecs);

            _logger.LogInformation(
                "前缀状态快照已记录，SystemHash={SystemHash}, SystemBytes={SystemBytes}, ToolCount={ToolCount}, ToolNamesHash={ToolNamesHash}, ToolSpecsBytes={ToolSpecsBytes}, EstimatedTokens={EstimatedTokens}",
                snapshot.SystemPromptHash, systemBytes, snapshot.ToolCount, snapshot.ToolNamesHash, toolSpecsBytes, estimatedTokens);

            return snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 检测缓存是否失效，对比快照与当前前缀状态并结合 token 用量判断
    /// </summary>
    public async Task<CacheBreakResult> CheckCacheBreakAsync(PromptStateSnapshot snapshot, TokenUsage usage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(usage);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentPrefix = new ImmutablePrefix(_staticSystemPrompt, _currentToolSpecs, []);
            var currentDynamicContent = string.Join("\n", _dynamicSystemMessages);
            var result = _cacheBreakDetector.CheckCacheBreak(snapshot, currentPrefix, currentDynamicContent, usage);

            if (result.BreakDetected)
            {
                _logger.LogWarning("缓存失效检测: Kind={Kind}, Detail={Detail}, CacheReadTokens={CacheReadTokens}",
                    result.Kind, result.Detail, usage.CacheReadInputTokens);
            }
            else
            {
                _logger.LogInformation("缓存失效检测: 无失效，前缀稳定，CacheReadTokens={CacheReadTokens}, CacheCreationTokens={CacheCreationTokens}",
                    usage.CacheReadInputTokens, usage.CacheCreationInputTokens);
            }

            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 获取已发现的工具集合
    /// </summary>
    public DiscoveredToolSet GetDiscoveredTools()
    {
        return _discoveredTools;
    }

    /// <summary>
    /// 获取延迟加载的工具信息列表（主要是 MCP 工具）
    /// </summary>
    public IReadOnlyList<DeferredToolInfo> GetDeferredTools()
    {
        return _deferredTools;
    }

    /// <summary>
    /// 从对话历史中提取已发现的工具名称并同步到已发现工具集合
    /// </summary>
    public async Task SyncDiscoveredToolsFromHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var history = AssembleMessages();
            var chatHistory = new MessageList(history);
            var discovered = ToolReferenceExtractor.ExtractDiscoveredToolNames(chatHistory);
            await _discoveredTools.DiscoverRangeAsync(discovered).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 异步释放资源，释放内部信号量
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
    }

    private List<ApiMessage> AssembleMessages()
    {
        var messages = new List<ApiMessage>();

        if (!string.IsNullOrWhiteSpace(_staticSystemPrompt))
        {
            messages.Add(new ApiMessage(MessageRole.System, _staticSystemPrompt));
        }

        var dynamicContent = string.Join("\n", _dynamicSystemMessages);
        var currentDynamicHash = string.IsNullOrEmpty(dynamicContent)
            ? string.Empty
            : ContentHash.Compute(dynamicContent);
        var dynamicChanged = currentDynamicHash != _previousDynamicHash;
        _previousDynamicHash = currentDynamicHash;

        foreach (var dynamicMsg in _dynamicSystemMessages)
        {
            if (dynamicChanged)
            {
                messages.Add(new ApiMessage(MessageRole.System, dynamicMsg, CacheBreakMarker.Create()));
            }
            else
            {
                messages.Add(new ApiMessage(MessageRole.System, dynamicMsg));
            }
        }

        foreach (var msg in _conversationLog.ToMessages())
        {
            messages.Add(msg);
        }

        return messages;
    }
}
