namespace Api.LLM.Fallback;

/// <summary>
/// 流式→非流式 fallback 装饰器 — 对齐 TS claude.ts 的 executeNonStreamingRequest + withRetry fallback 逻辑
/// 当流式请求失败（529过载/超时/不完整流/看门狗超时）时，自动降级为非流式请求
/// </summary>
/// <remarks>
/// 装饰器模式：包装任意 IQueryService 实现，透明添加 fallback 能力
/// 非流式调用直接委托给内部服务，流式调用包裹 fallback 逻辑
///
/// 注意：C# 不允许在 try-catch 块中使用 yield return（CS1626/CS1631），
/// 因此流式+fallback 逻辑通过 CollectStreamingEventsAsync 辅助方法实现：
/// 先收集流式事件到列表，如果失败则执行 fallback，最后统一 yield
/// </remarks>
public sealed class StreamingFallbackDecorator : IQueryService
{
    private readonly IQueryService _inner;
    private readonly StreamingFallbackConfig _config;
    private readonly ILogger? _logger;

    /// <summary>
    /// fallback 触发回调 — 对齐 TS Options.onStreamingFallback
    /// 调用方（如 QueryEngine）可在此标记 streamingFallbackOccured = true
    /// </summary>
    public event Action? OnStreamingFallback;

    /// <summary>
    /// 最近一次请求是否触发了 fallback（只读，供调用方查询）
    /// </summary>
    public bool LastRequestFellBack { get; private set; }

    public StreamingFallbackDecorator(
        IQueryService inner,
        StreamingFallbackConfig? config = null,
        ILogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _config = config ?? new StreamingFallbackConfig();
        _logger = logger;
    }

    /// <summary>
    /// 非流式调用 — 直接委托给内部服务（非流式不需要 fallback）
    /// </summary>
    public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
    {
        LastRequestFellBack = false;
        return _inner.GetApiMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
    }

    /// <summary>
    /// 流式调用 — 包裹看门狗 + fallback 逻辑
    /// 对齐 TS queryModelWithStreaming 的 catch 块 + executeNonStreamingRequest
    /// </summary>
    public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastRequestFellBack = false;

        if (!_config.Enabled)
        {
            _logger?.LogWarning("[DIAG-BYPASS] StreamingFallback bypassed, Enabled=false");
            await foreach (var evt in _inner.GetStreamEventContentsAsync(chatHistory, executionSettings, kernel, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }
            yield break;
        }

        var result = await CollectWithFallbackAsync(chatHistory, executionSettings, kernel, cancellationToken).ConfigureAwait(false);

        foreach (var evt in result.Events)
        {
            yield return evt;
        }
    }

    /// <summary>
    /// 收集流式事件 + fallback 逻辑 — 因 C# 不允许 try-catch 中 yield return，
    /// 将流式收集和 fallback 合并为一个返回列表的异步方法
    /// </summary>
    private async Task<StreamingResult> CollectWithFallbackAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings,
        IChatClient? kernel,
        CancellationToken cancellationToken)
    {
        using var watchdog = new StreamIdleWatchdog(
            _config.StreamIdleTimeoutMs,
            cancellationToken,
            _config.StreamWatchdogEnabled);

        var events = new List<StreamEvent>();

        try
        {
            await foreach (var evt in _inner.GetStreamEventContentsAsync(
                chatHistory, executionSettings, kernel, watchdog.CombinedToken).ConfigureAwait(false))
            {
                watchdog.Reset();
                events.Add(evt);
            }

            if (watchdog.WasIdleAborted)
            {
                throw new StreamingFallbackTriggeredException(
                    "Stream idle timeout - no chunks received",
                    FallbackCause.Watchdog);
            }

            if (events.Count == 0)
            {
                throw new StreamingFallbackTriggeredException(
                    "Stream completed without receiving any events",
                    FallbackCause.IncompleteStream);
            }

            return new StreamingResult(events, fellBack: false);
        }
        catch (OperationCanceledException ex) when (watchdog.WasIdleAborted)
        {
            return await ExecuteFallbackAsync(
                chatHistory, executionSettings, kernel, cancellationToken,
                new StreamingFallbackTriggeredException("Stream idle timeout", FallbackCause.Watchdog, ex),
                events);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new StreamingResult(events, fellBack: false);
        }
        catch (StreamingFallbackTriggeredException ex)
        {
            return await ExecuteFallbackAsync(chatHistory, executionSettings, kernel, cancellationToken, ex, events);
        }
        catch (Exception ex) when (ShouldFallback(ex, cancellationToken))
        {
            return await ExecuteFallbackAsync(chatHistory, executionSettings, kernel, cancellationToken, ex, events);
        }
    }

    /// <summary>
    /// 执行非流式 fallback — 对齐 TS executeNonStreamingRequest
    /// </summary>
    private async Task<StreamingResult> ExecuteFallbackAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings,
        IChatClient? kernel,
        CancellationToken cancellationToken,
        Exception originalError,
        List<StreamEvent> partialEvents)
    {
        _logger?.LogWarning(originalError, "Streaming request failed, falling back to non-streaming mode");

        LastRequestFellBack = true;
        OnStreamingFallback?.Invoke();

        var cappedSettings = AdjustSettingsForNonStreaming(executionSettings);

        using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        fallbackCts.CancelAfter(_config.NonStreamingTimeoutMs);

        IReadOnlyList<ApiMessage> messages;
        try
        {
            messages = await _inner.GetApiMessageContentsAsync(
                chatHistory, cappedSettings, kernel, fallbackCts.Token).ConfigureAwait(false);
        }
        catch (Exception fallbackEx)
        {
            _logger?.LogError(fallbackEx, "Non-streaming fallback also failed");
            throw new AggregateException("Both streaming and non-streaming fallback failed", originalError, fallbackEx);
        }

        var fallbackEvents = ConvertToStreamEvents(messages);
        return new StreamingResult(fallbackEvents, fellBack: true);
    }

    /// <summary>
    /// 判断异常是否应触发 fallback — 对齐 TS catch 块中的条件
    /// </summary>
    private bool ShouldFallback(Exception ex, CancellationToken originalToken)
    {
        if (originalToken.IsCancellationRequested)
            return false;

        if (ex is OperationCanceledException oce && oce.CancellationToken == originalToken)
            return false;

        if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
        {
            var statusCode = (int)httpEx.StatusCode.Value;
            if (_config.FallbackStatusCodes.Contains(statusCode))
                return true;
        }

        if (ex is ApiException apiEx && apiEx.StatusCode.HasValue)
        {
            if (_config.FallbackStatusCodes.Contains(apiEx.StatusCode.Value))
                return true;
        }

        return ex is TimeoutException
            || ex is TaskCanceledException
            || ex is HttpRequestException
            || ex is IOException;
    }

    /// <summary>
    /// 调整非流式请求参数 — 对齐 TS adjustParamsForNonStreaming
    /// 将 max_tokens 限制到 MaxNonStreamingTokens (64k)
    /// </summary>
    private ChatOptions? AdjustSettingsForNonStreaming(ChatOptions? settings)
    {
        if (settings is null)
            return null;

        var cappedMaxTokens = settings.MaxTokens.HasValue
            ? Math.Min(settings.MaxTokens.Value, _config.MaxNonStreamingTokens)
            : _config.MaxNonStreamingTokens;

        return new ChatOptions
        {
            FastModelId = settings.FastModelId,
            Temperature = settings.Temperature,
            MaxTokens = cappedMaxTokens,
            TopP = settings.TopP,
            FrequencyPenalty = settings.FrequencyPenalty,
            PresencePenalty = settings.PresencePenalty,
            ToolChoice = settings.ToolChoice,
            EffortLevel = settings.EffortLevel,
            FastMode = settings.FastMode,
        };
    }

    /// <summary>
    /// 将非流式 ApiMessage 转换为 StreamEvent 列表 — fallback 后需要统一为流式接口
    /// </summary>
    private static List<StreamEvent> ConvertToStreamEvents(IReadOnlyList<ApiMessage> messages)
    {
        var result = new List<StreamEvent>(messages.Count);

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            var metadata = new Dictionary<string, JsonElement>();

            if (msg.Metadata != null)
            {
                foreach (var kvp in msg.Metadata)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }

            metadata["StreamingFallback"] = JsonElementHelper.FromBoolean(true);

            if (i == messages.Count - 1)
            {
                metadata["FinishReason"] = JsonElementHelper.FromString("stop");
            }

            result.Add(new StreamEvent(msg.Role, msg.Content, msg.ModelId, metadata));
        }

        return result;
    }

    /// <summary>
    /// 流式收集结果 — 事件列表 + 是否触发过 fallback
    /// </summary>
    private sealed class StreamingResult(List<StreamEvent> events, bool fellBack)
    {
        public List<StreamEvent> Events { get; } = events;
        public bool FellBack { get; } = fellBack;
    }
}

/// <summary>
/// 流式 fallback 触发原因 — 对齐 TS fallback_cause 字段
/// </summary>
public enum FallbackCause
{
    /// <summary>看门狗超时（流长时间无数据）</summary>
    Watchdog,

    /// <summary>不完整流（无 message_start 或无 content blocks）</summary>
    IncompleteStream,

    /// <summary>404 流式端点（网关不支持流式）</summary>
    NotFound404,

    /// <summary>529 过载</summary>
    Overloaded529,

    /// <summary>其他错误（超时、IO 等）</summary>
    Other
}

/// <summary>
/// 流式 fallback 触发异常 — 内部使用，由 StreamingFallbackDecorator 抛出
/// 当看门狗超时或不完整流时抛出，触发非流式 fallback
/// </summary>
public sealed class StreamingFallbackTriggeredException : Exception
{
    public FallbackCause Cause { get; }

    public StreamingFallbackTriggeredException(string message, FallbackCause cause, Exception? innerException = null)
        : base(message, innerException)
    {
        Cause = cause;
    }
}
