namespace Api.LLM.Fallback;


/// <summary>
/// 流式→非流式 fallback 装饰器 — 对齐 TS claude.ts 的 executeNonStreamingRequest + withRetry fallback 逻辑
/// 当流式请求失败（529过载/超时/不完整流/看门狗超时）时，自动降级为非流式请求
/// </summary>
/// <remarks>
/// 装饰器模式：包装任意 IQueryService 实现，透明添加 fallback 能力
/// 非流式调用直接委托给内部服务，流式调用包裹 fallback 逻辑
///
/// 两阶段增量透传（ADR 0146 — TASK043 修复）：预热阶段（首事件前，可 fallback）+
/// 透传阶段（已产出后，异常上抛）——消除 CS1626/CS1631（try-catch 内禁 yield）的
/// 「全量收集后整发」旧实现（逐字流式失效/首字延迟=整轮时长/停止批量闪现三缺陷根因）
/// </remarks>
public sealed class StreamingFallbackDecorator : IQueryService {
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

    /// <summary>构造流式 fallback 装饰器。</summary>
    /// <param name="inner">内部查询服务。</param>
    /// <param name="config">fallback 配置。</param>
    /// <param name="logger">日志器。</param>
    public StreamingFallbackDecorator(
        IQueryService inner,
        StreamingFallbackConfig? config = null,
        ILogger? logger = null) {
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
        CancellationToken cancellationToken = default) {
        LastRequestFellBack = false;
        return _inner.GetApiMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
    }

    /// <summary>
    /// 流式调用 — 两阶段增量透传（ADR 0146）：
    /// 预热阶段拉首事件（可 fallback — 连接级错误拿不到任何事件时降级非流式无损重试）；
    /// 透传阶段逐事件 yield（不可 fallback — 已产出内容无法回收，中途失败上抛防重复输出）。
    /// </summary>
    /// <remarks>
    /// 消除 CS1626/CS1631（try-catch 内禁 yield）的手法：预热为非迭代器方法（可 try-catch），
    /// 透传为迭代器（仅 try-finally 做释放 — finally 中 await 合法）。
    /// watchdog 与 enumerator 所有权：预热成功时经 PrimeOutcome 转移给迭代器 finally 统一释放；
    /// fallback/取消路径由预热方法自释放。
    /// </remarks>
    public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        LastRequestFellBack = false;

        if (!_config.Enabled) {
            _logger?.LogInformation("[FALLBACK {CallId}] bypassed, Enabled=false", CallTrace.CurrentId);
            await foreach (var evt in _inner.GetStreamEventContentsAsync(chatHistory, executionSettings, kernel, cancellationToken).ConfigureAwait(false)) {
                yield return evt;
            }
            yield break;
        }

        var outcome = await TryPrimeAsync(chatHistory, executionSettings, kernel, cancellationToken).ConfigureAwait(false);

        if (outcome.FirstEvent is not { } first) {
            // Fallback/Cancelled 形态 — 非流式重试全量事件（取消=空列表零事件）
            foreach (var evt in outcome.FallbackEvents) {
                yield return evt;
            }
            yield break;
        }

        if (outcome.Enumerator is not { } enumerator || outcome.Watchdog is not { } watchdog) {
            // Primed 形态三件套由工厂保证同时非空 — 到达此处为内部状态损坏（开心路径断言屏蔽）
            throw new InvalidOperationException(
                "PrimeOutcome Primed 形态完整性破坏（FirstEvent 非空但 Enumerator/Watchdog 为空）— 请检查 TryPrimeAsync 的 Primed 工厂调用");
        }

        try {
            yield return first;
            watchdog.Reset();
            while (await enumerator.MoveNextAsync().ConfigureAwait(false)) {
                watchdog.Reset();
                yield return enumerator.Current;
            }
        } finally {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            watchdog.Dispose();
        }
    }

    /// <summary>
    /// 预热阶段 — 拉取首个上游事件（非迭代器，可 try-catch）：
    /// 空流（IncompleteStream）/看门狗超时/可 fallback 异常 → 非流式重试并返回全量事件；
    /// 用户取消 → 空结果（原语义 — 无可吐内容直接结束）；
    /// 成功 → 首事件 + 活的会话（watchdog/enumerator 转移给调用方释放）。
    /// </summary>
    private async Task<PrimeOutcome> TryPrimeAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings,
        IChatClient? kernel,
        CancellationToken cancellationToken) {
        var watchdog = new StreamIdleWatchdog(
            _config.StreamIdleTimeoutMs,
            cancellationToken,
            _config.StreamWatchdogEnabled);
        var enumerator = _inner.GetStreamEventContentsAsync(chatHistory, executionSettings, kernel, watchdog.CombinedToken)
            .GetAsyncEnumerator(cancellationToken);

        try {
            if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) {
                throw new StreamingFallbackTriggeredException(
                    "Stream completed without receiving any events",
                    FallbackCause.IncompleteStream);
            }
        } catch (OperationCanceledException ex) when (watchdog.WasIdleAborted) {
            await DisposeSessionAsync(enumerator, watchdog).ConfigureAwait(false);
            return await ExecuteFallbackAsync(
                chatHistory, executionSettings, kernel, cancellationToken,
                new StreamingFallbackTriggeredException("Stream idle timeout", FallbackCause.Watchdog, ex)).ConfigureAwait(false);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            await DisposeSessionAsync(enumerator, watchdog).ConfigureAwait(false);
            return PrimeOutcome.Cancelled();
        } catch (StreamingFallbackTriggeredException ex) {
            await DisposeSessionAsync(enumerator, watchdog).ConfigureAwait(false);
            return await ExecuteFallbackAsync(chatHistory, executionSettings, kernel, cancellationToken, ex).ConfigureAwait(false);
        } catch (Exception ex) when (ShouldFallback(ex, cancellationToken)) {
            await DisposeSessionAsync(enumerator, watchdog).ConfigureAwait(false);
            return await ExecuteFallbackAsync(chatHistory, executionSettings, kernel, cancellationToken, ex).ConfigureAwait(false);
        }

        return PrimeOutcome.Primed(enumerator.Current, enumerator, watchdog);
    }

    /// <summary>释放预热失败/取消路径的会话资源（成功路径所有权已转移，勿在此释放）。</summary>
    private static async Task DisposeSessionAsync(
        IAsyncEnumerator<StreamEvent> enumerator, StreamIdleWatchdog watchdog) {
        await enumerator.DisposeAsync().ConfigureAwait(false);
        watchdog.Dispose();
    }

    /// <summary>
    /// 执行非流式 fallback — 对齐 TS executeNonStreamingRequest（ADR 0146：仅预热阶段调用 — 首事件前失败才降级）
    /// </summary>
    private async Task<PrimeOutcome> ExecuteFallbackAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings,
        IChatClient? kernel,
        CancellationToken cancellationToken,
        Exception originalError) {
        _logger?.LogWarning("[FALLBACK {CallId}:161] 流式失败, 降级为非流式 | {ExType}: {Message}", CallTrace.CurrentId, originalError.GetType().Name, originalError.Message);

        LastRequestFellBack = true;
        OnStreamingFallback?.Invoke();

        var cappedSettings = AdjustSettingsForNonStreaming(executionSettings);

        using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        fallbackCts.CancelAfter(_config.NonStreamingTimeoutMs);

        IReadOnlyList<ApiMessage> messages;
        try {
            messages = await _inner.GetApiMessageContentsAsync(
                chatHistory, cappedSettings, kernel, fallbackCts.Token).ConfigureAwait(false);
        } catch (Exception fallbackEx) {
            _logger?.LogError("[FALLBACK {CallId}:179] 非流式降级也失败 | {ExType}: {Message}", CallTrace.CurrentId, fallbackEx.GetType().Name, fallbackEx.Message);
            throw new AggregateException("Both streaming and non-streaming fallback failed", originalError, fallbackEx);
        }

        var fallbackEvents = ConvertToStreamEvents(messages);
        return PrimeOutcome.FromFallback(fallbackEvents);
    }

    /// <summary>
    /// 判断异常是否应触发 fallback — 对齐 TS catch 块中的条件
    /// </summary>
    internal bool ShouldFallback(Exception ex, CancellationToken originalToken) {
        if (originalToken.IsCancellationRequested)
            return false;

        // 只有当 originalToken 可取消且异常确实携带该 token 时,才视为用户主动取消(不 fallback)
        // 修复:无 CancellationToken 的 OperationCanceledException/TaskCanceledException(CancellationToken=default=None)
        //      在 originalToken 为 None 时会被误判为用户取消而漏 fallback;要求 originalToken.CanBeCanceled 才排除
        if (ex is OperationCanceledException oce
            && originalToken.CanBeCanceled
            && oce.CancellationToken == originalToken)
            return false;

        if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue) {
            var statusCode = (int)httpEx.StatusCode.Value;
            if (_config.FallbackStatusCodes.Contains(statusCode))
                return true;
        }

        if (ex is ApiException apiEx && apiEx.StatusCode.HasValue) {
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
    internal ChatOptions? AdjustSettingsForNonStreaming(ChatOptions? settings) {
        if (settings is null)
            return null;

        var cappedMaxTokens = settings.MaxTokens.HasValue
            ? Math.Min(settings.MaxTokens.Value, _config.MaxNonStreamingTokens)
            : _config.MaxNonStreamingTokens;

        return new ChatOptions {
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
    internal static List<StreamEvent> ConvertToStreamEvents(IReadOnlyList<ApiMessage> messages) {
        var result = new List<StreamEvent>(messages.Count);

        for (var i = 0; i < messages.Count; i++) {
            var msg = messages[i];
            var metadata = new Dictionary<string, JsonElement>();

            if (msg.Metadata != null) {
                foreach (var kvp in msg.Metadata) {
                    metadata[kvp.Key] = kvp.Value;
                }
            }

            metadata["StreamingFallback"] = JsonElementHelper.FromBoolean(true);

            if (i == messages.Count - 1) {
                metadata["FinishReason"] = JsonElementHelper.FromString("stop");
            }

            result.Add(new StreamEvent(msg.Role, msg.Content, msg.ModelId, metadata));
        }

        return result;
    }

    /// <summary>
    /// 预热结果 — 三形态（ADR 0146）：
    /// Primed（首事件 + 活会话，透传阶段消费）/ Fallback（非流式重试全量事件）/ Cancelled（用户取消，空流结束）
    /// </summary>
    private sealed record PrimeOutcome(
        StreamEvent? FirstEvent,
        IAsyncEnumerator<StreamEvent>? Enumerator,
        StreamIdleWatchdog? Watchdog,
        List<StreamEvent> FallbackEvents) {
        /// <summary>构造 Primed 形态 — 首事件 + 活会话（三件套同时非空，透传阶段消费）。</summary>
        public static PrimeOutcome Primed(StreamEvent first, IAsyncEnumerator<StreamEvent> enumerator, StreamIdleWatchdog watchdog)
            => new(first, enumerator, watchdog, []);
        /// <summary>构造 Fallback 形态 — 非流式重试的全量事件（FirstEvent=null 为判别依据）。</summary>
        public static PrimeOutcome FromFallback(List<StreamEvent> events)
            => new(null, null, null, events);
        /// <summary>构造 Cancelled 形态 — 用户取消（零事件空流结束，语义同 Fallback 零事件）。</summary>
        public static PrimeOutcome Cancelled()
            => new(null, null, null, []);
    }
}

/// <summary>
/// 流式 fallback 触发原因 — 对齐 TS fallback_cause 字段
/// </summary>
public enum FallbackCause {
    /// <summary>看门狗超时（流长时间无数据）</summary>
    [EnumValue("watchdog")]
    Watchdog,

    /// <summary>不完整流（无 message_start 或无 content blocks）</summary>
    [EnumValue("incompleteStream")]
    IncompleteStream,

    /// <summary>404 流式端点（网关不支持流式）</summary>
    [EnumValue("notFound404")]
    NotFound404,

    /// <summary>529 过载</summary>
    [EnumValue("overloaded529")]
    Overloaded529,

    /// <summary>其他错误（超时、IO 等）</summary>
    [EnumValue("other")]
    Other,
}

/// <summary>
/// 流式 fallback 触发异常 — 内部使用，由 StreamingFallbackDecorator 抛出
/// 当看门狗超时或不完整流时抛出，触发非流式 fallback
/// </summary>
public sealed class StreamingFallbackTriggeredException : Exception {
    /// <summary>获取 fallback 触发原因。</summary>
    public FallbackCause Cause { get; }

    /// <summary>构造流式 fallback 触发异常。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="cause">触发原因。</param>
    /// <param name="innerException">内部异常。</param>
    public StreamingFallbackTriggeredException(string message, FallbackCause cause, Exception? innerException = null)
        : base(message, innerException) {
        Cause = cause;
    }
}