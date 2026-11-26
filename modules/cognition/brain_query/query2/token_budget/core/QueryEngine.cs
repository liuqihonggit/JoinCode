namespace Core.Query;

/// <summary>
/// QueryEngine.Create 方法的参数封装
/// </summary>
/// <param name="Kernel">LLM 客户端</param>
/// <param name="ToolRegistry">工具注册表</param>
/// <param name="Config">查询引擎配置（可选）</param>
/// <param name="Logger">日志记录器（可选）</param>
public sealed record QueryEngineOptions(
    IChatClient Kernel,
    IToolRegistry ToolRegistry,
    QueryEngineConfig? Config = null,
    ILogger<QueryEngine>? Logger = null);

/// <summary>
/// QueryEngine - 查询引擎，处理AI对话和工具调用
/// 参考 TS 原版的QueryEngine实现
/// 可选依赖通过 IQueryMiddleware 中间件管道注入，构造函数仅保留核心依赖
/// </summary>
[Register(typeof(IQueryEngine), ServiceLifetime.Singleton)]
public sealed partial class QueryEngine : ServiceEntity, IQueryEngine {
    private readonly IChatClient _kernel;
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolExecutionGateway? _toolExecutionGateway;
    private readonly ILogger<QueryEngine>? _logger;
    private readonly QueryEngineConfig _config;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly Lazy<MiddlewarePipeline<QueryMiddlewareContext>> _pipeline;

    /// <summary>
    /// DI 构造函数 — 查询管道通过 IServiceProvider 延迟解析，避免在构造期
    /// 实例化管道中间件导致循环依赖
    /// （IdleReminderMiddleware → IToolIdleReminderService → ITodoService →
    ///   ITaskRuntime → IWorkflowTaskExecutor → IAgentLifecycleManager → IQueryEngine → ♾️）
    /// </summary>
    /// <param name="kernel">LLM 客户端</param>
    /// <param name="toolRegistry">工具注册表</param>
    /// <param name="configOptions">查询引擎配置选项</param>
    /// <param name="serviceProvider">服务提供者（用于延迟解析中间件）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <param name="toolExecutionGateway">工具执行网关（可选）</param>
    public QueryEngine(
        IChatClient kernel,
        IToolRegistry toolRegistry,
        IOptions<QueryEngineConfig> configOptions,
        IServiceProvider? serviceProvider = null,
        ILogger<QueryEngine>? logger = null,
        ILoggerFactory? loggerFactory = null,
        IToolExecutionGateway? toolExecutionGateway = null) {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _toolExecutionGateway = toolExecutionGateway;
        _config = configOptions?.Value ?? new QueryEngineConfig();
        _serviceProvider = serviceProvider;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _pipeline = new Lazy<MiddlewarePipeline<QueryMiddlewareContext>>(BuildPipeline, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// 优先使用宿主装配的查询管道；独立宿主才从本地 DI 集合构建。
    /// </summary>
    private MiddlewarePipeline<QueryMiddlewareContext> BuildPipeline() {
        if (_serviceProvider?.GetService<MiddlewarePipeline<QueryMiddlewareContext>>() is { } composed) {
            return composed;
        }

        var allMiddlewares = new List<IMiddleware<QueryMiddlewareContext>>();
        if (_serviceProvider is not null) {
            var middlewares = _serviceProvider.GetService<IEnumerable<IQueryMiddleware>>();
            if (middlewares is not null)
                allMiddlewares.AddRange(middlewares);
        }
        allMiddlewares.Add(QueryCoreMiddleware.Instance);
        return _loggerFactory is not null
            ? new PipelineBuilder<QueryMiddlewareContext>()
                .WithLoggingScope(_loggerFactory)
                .UseRange(allMiddlewares)
                .Build()
            : new MiddlewarePipeline<QueryMiddlewareContext>(allMiddlewares);
    }

    /// <summary>
    /// 创建带默认配置的QueryEngine（无 DI 中间件，用于测试/非 DI 场景）
    /// </summary>
    /// <param name="options">创建参数</param>
    /// <returns>QueryEngine 实例</returns>
    public static QueryEngine Create(QueryEngineOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        return new QueryEngine(
            options.Kernel,
            options.ToolRegistry,
            Microsoft.Extensions.Options.Options.Create(options.Config ?? new QueryEngineConfig()),
            serviceProvider: null,
            logger: options.Logger);
    }

    /// <summary>
    /// 执行查询
    /// </summary>
    /// <param name="userInput">用户输入</param>
    /// <param name="chatHistory">对话历史</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询流块异步枚举</returns>
    public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var chunk in QueryAsync(userInput, chatHistory, options: null, cancellationToken).ConfigureAwait(false)) {
            yield return chunk;
        }
    }

    /// <summary>
    /// 执行查询（带工具过滤选项）
    /// </summary>
    /// <param name="userInput">用户输入</param>
    /// <param name="chatHistory">对话历史</param>
    /// <param name="options">查询选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询流块异步枚举</returns>
    public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        QueryOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var context = new QueryMiddlewareContext {
            UserInput = userInput,
            ChatHistory = chatHistory,
            Options = options,
            Config = _config,
            Kernel = _kernel,
            ToolRegistry = _toolRegistry,
            Logger = _logger,
            ExecuteCoreAsync = ExecuteCoreLoopAsync,
        };

        chatHistory.AddUserMessage(userInput);
        _logger?.LogInformation("[QueryEngine] 开始处理查询: {Input}", userInput);

        // TASK071 query2-④ — 预算注入（对齐 TS taskBudget）：Options.TokenBudget 为正则向预算管理器分配，
        // 驱动 ConsumeTokens/HistorySnip 链（旧 AllocateBudgetAsync 零调用 → GetRemaining 恒 MaxValue → 裁剪死代码）
        if (options?.TokenBudget is > 0 && _serviceProvider?.GetService<ITokenBudgetManager>() is { } budgetManager) {
            await budgetManager.AllocateBudgetAsync(options.TokenBudget.Value, cancellationToken).ConfigureAwait(false);
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = ProduceOutputAsync(context, lifetime.Token);
        var drained = false;
        try {
            // 等生产者关闭通道再结束，确保取消时历史补写已完成。
            await foreach (var chunk in context.OutputReader.ReadAllAsync().ConfigureAwait(false))
                yield return chunk;
            drained = true;
        } finally {
            await FinishOutputAsync(producer, lifetime, drained, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>管道离开时关闭输出以唤醒读者；原异常由生产者任务保留。</summary>
    private async Task ProduceOutputAsync(QueryMiddlewareContext context, CancellationToken ct) {
        try {
            await _pipeline.Value.ExecuteAsync(context, ct).ConfigureAwait(false);
        } finally {
            context.CompleteOutput();
        }
    }

    /// <summary>提前退出时停止并等待生产者；正常消费时传播原生异常或取消。</summary>
    private static async Task FinishOutputAsync(Task producer, CancellationTokenSource lifetime, bool drained, CancellationToken callerToken) {
        var stoppedByReader = !drained && !producer.IsCompleted && !callerToken.IsCancellationRequested;
        if (stoppedByReader)
            await lifetime.CancelAsync().ConfigureAwait(false);

        try {
            await producer.ConfigureAwait(false);
        } catch (OperationCanceledException) when (stoppedByReader && !callerToken.IsCancellationRequested) {
            // 主动停止枚举不是调用方取消；等待生产者收尾后正常释放。
        }
    }

    /// <summary>
    /// 核心执行循环 — 由 QueryCoreMiddleware 调用
    /// <para>Stop hook 回灌上限由 <see cref="QueryEngineConfig.MaxStopHookReplyRounds"/> 提供
    ///（TASK071 query2-③ 防阻断/回灌死循环烧钱；TASK102 后续从硬编码改为可配）。</para>
    /// </summary>
    private async Task ExecuteCoreLoopAsync(QueryMiddlewareContext context, CancellationToken cancellationToken) {
        var retryCount = 0;
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var totalCacheRead = 0;
        var totalCacheCreation = 0;
        var totalReasoning = 0;
        var stopHookReplies = 0;
        var totalTurns = 0;
        // TASK102/TASK132 轮次闸门：状态机单源（计数器 + 推荐 + 熔断 + 完成终验）与已拦截收尾次数
        var roundGateSession = new RoundGateSession(RoundGatePolicy.ReadStrictMode());
        var roundGateRejections = 0;
        var requireRoundGate = context.Options is { RequireRoundGate: true }
            && context.Options.IsToolAllowed(RoundGatePolicy.ToolName);

        while (context.TotalToolCalls < _config.MaxToolCallIterations) {
            cancellationToken.ThrowIfCancellationRequested();
            totalTurns++;

            // 迭代开始回调（TASK103 — ChatService 查询后端步首注入 steer 输入）
            if (context.Options?.OnIterationStart is { } onIterationStart) {
                await onIterationStart(context.ChatHistory, cancellationToken).ConfigureAwait(false);
            }

            // 迭代前钩子（USD 预算检查等）
            foreach (var hook in context.BeforeIterationHooks) {
                await hook(context, cancellationToken).ConfigureAwait(false);
            }

            if (context.ShouldStop) {
                break;
            }

            QueryIterationResult? iterationResult = null;
            var success = false;
            context.HasEmittedStreamContent = false;

            // 重试机制 — 已发出流式内容的部分输出不再整轮重试（否则重复文本上屏）
            while (!success && retryCount <= _config.Retry.MaxRetries) {
                try {
                    iterationResult = await ExecuteIterationInternalAsync(
                        context, cancellationToken).ConfigureAwait(false);
                    success = true;
                    retryCount = 0;
                } catch (Exception ex) when (retryCount < _config.Retry.MaxRetries && IsRetryable(ex) && !context.HasEmittedStreamContent) {
                    retryCount++;
                    var delay = CalculateRetryDelay(retryCount);
                    _logger?.LogWarning(ex, "[QueryEngine] 迭代执行失败，{RetryCount}/{MaxRetries} 次重试，等待 {DelayMs}ms",
                        retryCount, _config.Retry.MaxRetries, delay);
                    NotifyRetry(context, ex.Message, retryCount, _config.Retry.MaxRetries, delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            if (!success || iterationResult == null) {
                context.PublishOutput(BuildRetryExhaustedErrorChunk(_config.Retry.MaxRetries));
                return;
            }

            // 更新上下文的 Token 使用信息（最后一轮值 — 钩子消费语义不变）+ 跨轮累计（P7 权威校正数据源）
            context.InputTokens = iterationResult.InputTokens;
            context.OutputTokens = iterationResult.OutputTokens;
            totalInputTokens += iterationResult.InputTokens;
            totalOutputTokens += iterationResult.OutputTokens;
            if (iterationResult.Usage is { } iterUsage) {
                totalCacheRead += iterUsage.CacheReadInputTokens;
                totalCacheCreation += iterUsage.CacheCreationInputTokens;
                totalReasoning += iterUsage.ReasoningTokens;
            }
            context.ToolName = iterationResult.ToolCall?.ToolName;
            context.HasToolCall = iterationResult.ToolCall is not null;

            // LLM 调用后钩子（Token 预算消耗、成本追踪等）
            foreach (var hook in context.AfterLlmCallHooks) {
                await hook(context, cancellationToken).ConfigureAwait(false);
            }

            // 异常输出已注入纠正提示词 — 不进入收尾判定，直接下一轮重试
            if (iterationResult.RequestsAnotherTurn) {
                continue;
            }

            // 处理查询完成（无工具调用）
            if (iterationResult.ToolCalls.Count == 0) {
                // TASK102/TASK132 轮次闸门：未通过状态机终验 → 回灌阻断消息并继续一轮（独立于 Stop hook 计数）
                switch (EvaluateRoundGateStop(requireRoundGate, roundGateSession, ref roundGateRejections, out var gateMaxRejections, out var gateVerdict)) {
                    case RoundGateStopDecision.Blocked:
                        context.ChatHistory.AddUserMessage(
                            RoundGatePrompts.BuildStopBlockedMessage(
                                roundGateRejections, gateMaxRejections,
                                gateVerdict.Reason ?? "未通过闸门终验", roundGateSession.Recommend()));
                        context.IsQueryComplete = false;
                        continue;

                    case RoundGateStopDecision.Exhausted:
                        context.PublishOutput(new QueryStreamChunk {
                            Type = AgentStreamChunkType.Content,
                            Content = $"⚠ 轮次闸门连续 {roundGateRejections} 次未通过（上限 {gateMaxRejections}），本轮已强制结束。"
                        });
                        break;
                }

                // 轮内注入未消费完时不得收尾（TASK103 — 对齐 chat 循环 HasPendingSteerInput）
                if (context.Options?.HasPendingIterationInput?.Invoke() == true) {
                    context.IsQueryComplete = false;
                    continue;
                }

                context.Stopwatch.Stop();
                context.IsQueryComplete = true;

                // 查询完成钩子（空闲提醒、停止 Hook 等）
                foreach (var hook in context.OnCompleteHooks) {
                    await hook(context, cancellationToken).ConfigureAwait(false);
                }

                // TASK071 query2-③ — 对齐 TS stopHooks.ts:257-263 + 296-309：Stop hook 阻断消息
                // 以 user 消息回灌并继续一轮迭代（模型收到阻断说明后继续收尾），上限 5 轮防无限烧钱
                if (context.StopHookBlockingMessages.Count > 0 && stopHookReplies < _config.MaxStopHookReplyRounds) {
                    stopHookReplies++;
                    foreach (var blockMessage in context.StopHookBlockingMessages) {
                        _logger?.LogInformation("[QueryEngine] Stop hook 阻断回灌 ({Reply}/{Max}): {Message}",
                            stopHookReplies, _config.MaxStopHookReplyRounds, blockMessage);
                        context.ChatHistory.AddUserMessage(blockMessage);
                    }

                    context.StopHookBlockingMessages.Clear();
                    context.IsQueryComplete = false;
                    continue;
                }

                _logger?.LogInformation("[QueryEngine] 查询完成，耗时 {ElapsedMs}ms", context.Stopwatch.ElapsedMilliseconds);

                var content = iterationResult.Content ?? string.Empty;
                // P7：累计 usage 随 Complete chunk 透传 — metadata 未提供 token 数时保持 null（禁伪 0）
                var usage = (totalInputTokens > 0 || totalOutputTokens > 0)
                    ? new JoinCode.Abstractions.LLM.Chat.TokenUsage {
                        PromptTokens = totalInputTokens,
                        CompletionTokens = totalOutputTokens,
                        CacheReadInputTokens = totalCacheRead,
                        CacheCreationInputTokens = totalCacheCreation,
                        ReasoningTokens = totalReasoning,
                    }
                    : null;
                // duration_api_ms 取 API 调用累计耗时（TASK103 #46）；墙上时间在日志里单独可见。
                context.PublishOutput(BuildCompletionChunk(content, context.ApiDurationMs, context.TotalToolCalls, totalTurns, context.TotalCostUsd, BuildCacheSafeParams(context), usage, MapFinishReasonToStopReason(iterationResult.FinishReason), iterationResult.ModelId, context.StructuredOutputFromTool));
                return;
            }

            // 依次执行本轮所有工具调用 — 并发安全工具合批并行，非并发安全工具独占批次（ADR 0160）
            var scheduledToolCalls = ScheduleToolCalls(iterationResult.ToolCalls, _config.MaxToolCallIterations - context.TotalToolCalls);
            var concurrencyClassifier = _serviceProvider?.GetService<IToolConcurrencyClassifier>();
            var batchStart = 0;
            var stopProcessing = false;
            while (batchStart < scheduledToolCalls.Count && !stopProcessing) {
                var batchEnd = await DetermineBatchEndAsync(scheduledToolCalls, batchStart, concurrencyClassifier, cancellationToken).ConfigureAwait(false);
                if (batchEnd - batchStart > 1) {
                    Diag.WriteLine($"[TOOL-PARALLEL] batch={batchEnd - batchStart} tools={string.Join(',', scheduledToolCalls.Skip(batchStart).Take(batchEnd - batchStart).Select(x => x.Call.ToolName))}");
                }
                var firstCallNumber = context.TotalToolCalls + 1;

                for (var i = batchStart; i < batchEnd; i++) {
                    var toolCall = scheduledToolCalls[i].Call;
                    var callNumber = firstCallNumber + (i - batchStart);
                    context.ToolName = toolCall.ToolName;
                    context.HasToolCall = true;

                    context.PublishOutput(new QueryStreamChunk {
                        Type = AgentStreamChunkType.ToolCallStart,
                        ToolName = toolCall.ToolName,
                        ToolCallId = toolCall.ToolCallId,
                        ToolArguments = toolCall.RawArguments,
                        ToolCallNumber = callNumber
                    });
                }

                context.TotalToolCalls = firstCallNumber + (batchEnd - batchStart) - 1;

                // 批次内先全部启动，再按原调用顺序逐条 await — 并行收益 + 历史/事件顺序与串行一致
                var batchTasks = new Task<ToolResult?>[batchEnd - batchStart];
                for (var i = 0; i < batchTasks.Length; i++) {
                    batchTasks[i] = TryExecuteToolAsync(scheduledToolCalls[batchStart + i].Call, context.Options, cancellationToken);
                }

                for (var i = 0; i < batchTasks.Length; i++) {
                    var scheduled = scheduledToolCalls[batchStart + i];
                    var toolCall = scheduled.Call;
                    var callNumber = firstCallNumber + i;
                    var toolResult = await batchTasks[i].ConfigureAwait(false);
                    if (toolResult is null) {
                        // 取消时未完成的工具调用补写 (aborted) — assistant 工具调用消息后必须有对应结果，历史才对下一次请求合法
                        WriteAbortedToolResults(context, iterationResult.ToolCalls, scheduled.OriginalIndex, iterationResult.Content);
                        throw new OperationCanceledException(cancellationToken);
                    }

                    // TASK102/TASK132 轮次闸门：记录选择 → 更新状态机（计数/熔断/申诉）→ 把推荐与熔断提示并入工具结果
                    // 报错的闸门调用（参数非法等）不算有效选择，不能推进状态机，否则会误记一次失败自验
                    if (!toolResult.IsError && RoundGatePolicy.TryReadInvocation(toolCall.ToolName, toolCall.Arguments, toolCall.RawArguments, out var gateInvocation)) {
                        var gateStep = roundGateSession.Record(gateInvocation);
                        toolResult = AppendRoundGateAdvisory(toolResult, gateStep);
                        _logger?.LogInformation("[RoundGate] choice={Choice} recommended={Recommended} appeal={IsAppeal} fuse={Fuse}",
                            gateStep.Choice.ToValue(), gateStep.Recommendation.Choice.ToValue(),
                            gateStep.IsAppeal, gateStep.Fuse?.ForcedChoice.ToValue() ?? "-");
                    }

                    context.PublishOutput(BuildToolCallEndChunk(toolCall.ToolName, toolCall.ToolCallId, toolResult, callNumber));

                    // 将工具结果添加到对话历史（assistant 文本与工具调用同轮保留，对齐 chat 循环）
                    await AddToolResultToHistory(context, toolCall, toolResult, iterationResult.Content).ConfigureAwait(false);

                    // TASK071-C6 — 结构化输出捕获：对齐 TS QueryEngine structuredOutputFromTool
                    // 合成工具验证通过时 ToolResult.StructuredOutput 携带验证数据，捕获到 context 供 result 消息填充
                    if (toolResult.StructuredOutput is { } structuredOutput) {
                        context.StructuredOutputFromTool = structuredOutput;
                    }

                    // TASK071-C6 — 合成工具调用计数 + 重试上限（对齐 TS MAX_STRUCTURED_OUTPUT_RETRIES）
                    if (toolCall.ToolName == SystemToolNameEnumConstants.SyntheticOutput) {
                        context.StructuredOutputCallCount++;
                        if (context.StructuredOutputCallCount >= context.MaxStructuredOutputRetries) {
                            _logger?.LogWarning("[QueryEngine] StructuredOutput 合成工具调用次数超限: {Count}/{Max}",
                                context.StructuredOutputCallCount, context.MaxStructuredOutputRetries);
                            context.PublishOutput(BuildStructuredOutputRetryExhaustedErrorChunk(context.MaxStructuredOutputRetries));
                            return;
                        }
                    }

                    // 子代理 usage 累计 — agent 工具的 ToolResult.Usage 携带子代理 token 用量（含缓存命中）
                    if (toolResult.Usage is { } subagentUsage) {
                        totalInputTokens += subagentUsage.PromptTokens;
                        totalOutputTokens += subagentUsage.CompletionTokens;
                        totalCacheRead += subagentUsage.CacheReadInputTokens;
                        totalCacheCreation += subagentUsage.CacheCreationInputTokens;
                        totalReasoning += subagentUsage.ReasoningTokens;
                    }

                    // 工具调用后钩子（内容替换预算检查、递减回报检测、历史裁剪、空闲提醒等）
                    foreach (var hook in context.AfterToolCallHooks) {
                        await hook(context, cancellationToken).ConfigureAwait(false);
                    }

                    if (context.ShouldBreak) {
                        stopProcessing = true;
                        break;
                    }
                }

                batchStart = batchEnd;
            }

            if (context.ShouldBreak) {
                break;
            }
        }

        _logger?.LogWarning("[QueryEngine] 达到最大工具调用次数限制: {Max}", _config.MaxToolCallIterations);
        context.PublishOutput(BuildMaxIterationErrorChunk(_config.MaxToolCallIterations));
    }

    /// <summary>轮次闸门收尾裁决结果</summary>
    private enum RoundGateStopDecision { Allowed, Blocked, Exhausted }

    /// <summary>
    /// TASK102/TASK132 轮次闸门 — 用状态机裁决本次收尾：未过终验时累计拦截次数并返回 Blocked；
    /// 达到拦截上限返回 Exhausted（放行并提示），避免模型死不调用闸门时无限燃烧 token。
    /// </summary>
    /// <param name="required">本查询是否启用闸门。</param>
    /// <param name="session">本轮闸门状态机。</param>
    /// <param name="rejections">已拦截次数（拦截时自增）。</param>
    /// <param name="maxRejections">拦截上限（≤0 表示不限制）。</param>
    /// <param name="verdict">状态机裁决明细（拦截原因）。</param>
    /// <returns>收尾裁决。</returns>
    private RoundGateStopDecision EvaluateRoundGateStop(
        bool required, RoundGateSession session, ref int rejections, out int maxRejections, out RoundGateStopVerdict verdict) {
        maxRejections = RoundGatePolicy.ReadMaxStopRejections();
        verdict = session.EvaluateStop(required);
        if (verdict.Allowed)
            return RoundGateStopDecision.Allowed;

        if (!RoundGatePolicy.CanRejectStop(rejections, maxRejections))
            return RoundGateStopDecision.Exhausted;

        rejections++;
        _logger?.LogWarning("[QueryEngine] 轮次闸门拦截收尾（第 {Count}/{Max} 次）：{Reason}",
            rejections, maxRejections, verdict.Reason ?? "-");
        return RoundGateStopDecision.Blocked;
    }

    /// <summary>
    /// 把闸门状态机提示追加到工具结果 — 让 AI 在本轮就看到"推荐 / 备选 / 申诉 / 熔断"。
    /// </summary>
    /// <param name="result">原始工具结果。</param>
    /// <param name="step">本轮闸门状态快照。</param>
    /// <returns>追加提示后的工具结果。</returns>
    private static ToolResult AppendRoundGateAdvisory(ToolResult result, RoundGateStep step) {
        var content = result.Content.ToList();
        content.Add(new ToolContent {
            Type = ToolContentType.Text,
            Text = RoundGatePrompts.BuildStateAdvisory(step),
        });
        return result with { Content = content };
    }

    /// <summary>
    /// 构建达到最大工具调用次数限制的错误 chunk — 纯函数（TASK031 阶段2.1 提取）
    /// </summary>
    /// <param name="maxIterations">最大迭代次数</param>
    /// <returns>错误类型的 QueryStreamChunk</returns>
    internal static QueryStreamChunk BuildMaxIterationErrorChunk(int maxIterations) {
        return new QueryStreamChunk {
            Type = AgentStreamChunkType.Error,
            Content = $"达到最大工具调用次数限制 ({maxIterations})",
            StopReason = "error_max_turns"
        };
    }

    /// <summary>
    /// 构建重试耗尽的错误 chunk — 纯函数（TASK031 阶段2.1 提取）
    /// </summary>
    /// <param name="maxRetries">最大重试次数</param>
    /// <returns>错误类型的 QueryStreamChunk</returns>
    internal static QueryStreamChunk BuildRetryExhaustedErrorChunk(int maxRetries) {
        return new QueryStreamChunk {
            Type = AgentStreamChunkType.Error,
            Content = $"执行失败，已达到最大重试次数 ({maxRetries})",
            StopReason = "error_max_retries"
        };
    }

    /// <summary>
    /// 构建结构化输出重试耗尽的错误 chunk — 对齐 TS error_max_structured_output_retries
    /// </summary>
    /// <param name="maxRetries">合成工具最大重试次数</param>
    /// <returns>错误类型的 QueryStreamChunk</returns>
    internal static QueryStreamChunk BuildStructuredOutputRetryExhaustedErrorChunk(int maxRetries) {
        return new QueryStreamChunk {
            Type = AgentStreamChunkType.Error,
            Content = $"Structured output validation failed after {maxRetries} attempts",
            StopReason = "error_max_structured_output_retries"
        };
    }

    /// <summary>
    /// 构建查询完成的 chunk — 纯函数（TASK031 阶段2.1 提取；TASK063 P7 补 usage 透传）
    /// </summary>
    /// <param name="content">完成内容</param>
    /// <param name="elapsedMs">LLM API 调用累计耗时毫秒（duration_api_ms）</param>
    /// <param name="totalToolCalls">总工具调用次数</param>
    /// <param name="totalTurns">LLM 调用轮次数（对齐 ClaudeCode SDK num_turns 语义）</param>
    /// <param name="costUsd">USD 成本</param>
    /// <param name="cacheSafeParams">缓存安全参数</param>
    /// <param name="usage">累计 token 用量 — 沿链透传到子代理 AgentFinished 事件（P7 完成态权威校正数据源）</param>
    /// <param name="stopReason">查询终止原因（end_turn/max_tokens/content_filter/tool_calls），null 默认 end_turn</param>
    /// <param name="modelId">模型标识（从流式 chunk 提取），null = 未提供</param>
    /// <param name="structuredOutput">结构化输出数据（合成工具验证通过时携带），null = 无结构化输出</param>
    /// <returns>完成类型的 QueryStreamChunk</returns>
    internal static QueryStreamChunk BuildCompletionChunk(string content, long elapsedMs, int totalToolCalls, int totalTurns, decimal costUsd, JoinCode.Abstractions.LLM.Chat.CacheSafeParams? cacheSafeParams, JoinCode.Abstractions.LLM.Chat.TokenUsage? usage = null, string? stopReason = null, string? modelId = null, System.Text.Json.JsonElement? structuredOutput = null) {
        return new QueryStreamChunk {
            Type = AgentStreamChunkType.Complete,
            Content = content,
            StopReason = stopReason ?? "end_turn",
            ExecutionTimeMs = elapsedMs,
            TotalToolCalls = totalToolCalls,
            TotalTurns = totalTurns,
            CostUsd = costUsd,
            CacheSafeParams = cacheSafeParams,
            Usage = usage,
            ModelId = modelId,
            StructuredOutput = structuredOutput
        };
    }

    /// <summary>将 OpenAI finish_reason 映射为查询域 StopReason</summary>
    private static string? MapFinishReasonToStopReason(string? finishReason) => finishReason switch {
        "stop" => "end_turn",
        "length" => "max_tokens",
        "content_filter" => "content_filter",
        "tool_calls" => "tool_use",
        _ => null
    };

    /// <summary>
    /// 应用聊天侧执行设置覆盖（TASK103 灰度后端）— 覆盖项优先，未提供项保持查询配置。
    /// </summary>
    /// <param name="settings">查询引擎按自身配置构造的基础设置。</param>
    /// <param name="overrides">聊天侧 ChatOptionsFactory 产物；null 表示不覆盖。</param>
    /// <returns>合并后的执行设置。</returns>
    private static ChatOptions ApplyExecutionOverrides(ChatOptions settings, ChatOptions? overrides) {
        if (overrides is null)
            return settings;

        return new ChatOptions {
            Temperature = overrides.Temperature ?? settings.Temperature,
            MaxTokens = overrides.MaxTokens ?? settings.MaxTokens,
            TopP = overrides.TopP ?? settings.TopP,
            FrequencyPenalty = overrides.FrequencyPenalty ?? settings.FrequencyPenalty,
            PresencePenalty = overrides.PresencePenalty ?? settings.PresencePenalty,
            ToolChoice = overrides.ToolChoice,
            DiscoveredTools = overrides.DiscoveredTools ?? settings.DiscoveredTools,
            DeferredTools = overrides.DeferredTools.Count > 0 ? overrides.DeferredTools : settings.DeferredTools,
            ExtensionData = overrides.ExtensionData.Count > 0 ? overrides.ExtensionData : settings.ExtensionData,
            EffortLevel = overrides.EffortLevel ?? settings.EffortLevel,
            ThinkingEnabled = overrides.ThinkingEnabled || settings.ThinkingEnabled,
            FastMode = overrides.FastMode || settings.FastMode,
            FastModelId = overrides.FastModelId ?? settings.FastModelId,
            PromptCacheKey = overrides.PromptCacheKey ?? settings.PromptCacheKey,
            ContextManagement = overrides.ContextManagement ?? settings.ContextManagement,
        };
    }

    /// <summary>
    /// 复制执行设置并覆盖 effort/model — 保留延迟工具、思考开关等其余字段（旧实现重建对象时丢失）。
    /// </summary>
    /// <param name="settings">原执行设置。</param>
    /// <param name="effortLevel">推理力度覆盖；null 保持原值。</param>
    /// <param name="modelId">模型覆盖；非空时同时写入 ExtensionData["model"] 并启用 FastMode。</param>
    /// <returns>覆盖后的执行设置。</returns>
    private static ChatOptions CloneExecutionSettings(ChatOptions settings, JoinCode.Abstractions.LLM.EffortLevel? effortLevel = null, string? modelId = null) {
        var extensionData = settings.ExtensionData;
        if (!string.IsNullOrEmpty(modelId)) {
            extensionData = new Dictionary<string, System.Text.Json.JsonElement>(settings.ExtensionData) {
                ["model"] = JoinCode.Abstractions.Utils.JsonElementHelper.FromString(modelId)
            };
        }

        return new ChatOptions {
            Temperature = settings.Temperature,
            MaxTokens = settings.MaxTokens,
            TopP = settings.TopP,
            FrequencyPenalty = settings.FrequencyPenalty,
            PresencePenalty = settings.PresencePenalty,
            ToolChoice = settings.ToolChoice,
            DiscoveredTools = settings.DiscoveredTools,
            DeferredTools = settings.DeferredTools,
            ExtensionData = extensionData,
            EffortLevel = effortLevel ?? settings.EffortLevel,
            ThinkingEnabled = settings.ThinkingEnabled,
            FastMode = !string.IsNullOrEmpty(modelId) || settings.FastMode,
            FastModelId = modelId ?? settings.FastModelId,
            PromptCacheKey = settings.PromptCacheKey,
            ContextManagement = settings.ContextManagement,
        };
    }

    private async Task<QueryIterationResult> ExecuteIterationInternalAsync(
        QueryMiddlewareContext context,
        CancellationToken cancellationToken) {
        var parameters = new LlmParameters {
            Temperature = _config.Temperature,
            MaxTokens = _config.MaxTokens,
            TopP = _config.TopP
        };
        var executionSettings = parameters.ToExecutionSettings(ToolChoice.AutoInvoke);
        // TASK103：ChatService 灰度后端透传原聊天执行设置（温度/思考/延迟工具等），
        // 随后仍允许 QueryOptions 的显式 effort/model 覆盖。
        executionSettings = ApplyExecutionOverrides(executionSettings, context.Options?.ExecutionOverrides);

        // 对齐 TS executeForkedSkill: 传递 effort 给子智能体
        // 如果 QueryOptions 指定了 EffortLevel，覆盖默认值
        if (context.Options?.EffortLevel is { } effortLevel) {
            executionSettings = CloneExecutionSettings(executionSettings, effortLevel: effortLevel);
        }

        // 对齐 Reasonix Coordinator: 双模型分离 — 按请求指定模型
        if (!string.IsNullOrEmpty(context.Options?.ModelId)) {
            executionSettings = CloneExecutionSettings(executionSettings, modelId: context.Options.ModelId);
        }

        var chatCompletionService = _kernel.GetChatCompletionService();
        var responseBuilder = new StringBuilder();
        var pendingToolCalls = new List<ToolCallRequest>();
        var inputTokens = 0;
        var outputTokens = 0;
        JoinCode.Abstractions.LLM.Chat.TokenUsage? streamUsage = null;
        var lastFinishReason = (string?)null;
        string? streamModelId = null;

        // TASK103 #46 — duration_api_ms 必须是 LLM API 调用累计耗时，不是整轮查询墙上时间。
        // try/finally 保证提前 return（工具调用分支）与取消路径都计入本轮 API 耗时。
        var apiStopwatch = Stopwatch.StartNew();
        try {
        await foreach (var streamChunk in chatCompletionService.GetStreamEventContentsAsync(
            context.ChatHistory, executionSettings, _kernel, cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            var content = streamChunk.Content ?? string.Empty;

            streamModelId ??= streamChunk.ModelId;

            if (streamChunk.Metadata?.TryGetValue("FinishReason", out var frEl) == true && frEl.ValueKind == JsonValueKind.String) {
                var fr = frEl.GetString();
                if (!string.IsNullOrEmpty(fr)) lastFinishReason = fr;
            }

            // 检查工具调用 — 统一读 AllToolCalls 数组（含0~N个工具调用）
            if (streamChunk.Metadata?.TryGetValue("AllToolCalls", out var allEl) == true &&
                allEl.ValueKind == JsonValueKind.Array &&
                allEl.GetArrayLength() > 0) {
                foreach (var item in allEl.EnumerateArray()) {
                    string? id = null;
                    string? name = null;
                    var arguments = "{}";

                    if (item.TryGetProperty("Id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                        id = idProp.GetString();
                    if (item.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                        name = nameProp.GetString();
                    if (item.TryGetProperty("Arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String)
                        arguments = argsProp.GetString() ?? "{}";
                    if (string.IsNullOrWhiteSpace(arguments)) arguments = "{}";

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var repairedName = LlmJsonHelper.RepairToolName(name, _logger);
                    if (string.IsNullOrEmpty(repairedName)) continue;

                    pendingToolCalls.Add(new ToolCallRequest {
                        ToolName = repairedName,
                        ToolCallId = id,
                        RawArguments = arguments,
                        Arguments = ExtractArguments(streamChunk.Metadata, arguments)
                    });
                }
            }

            // 累积内容并立即上屏 — 区分 thinking/reasoning_content 与普通 text；
            // TASK103 流式对齐：边收边发（旧实现整轮聚合后一次性入队，GUI 时间差≈0）
            if (!string.IsNullOrEmpty(content)) {
                var isThinking = streamChunk.Metadata?.ContainsKey("thinking_content") == true
                    || streamChunk.Metadata?.ContainsKey("reasoning_content") == true;
                context.HasEmittedStreamContent = true;
                if (isThinking) {
                    context.PublishOutput(new QueryStreamChunk {
                        Type = AgentStreamChunkType.Thinking,
                        Content = content
                    });
                } else {
                    responseBuilder.Append(content);
                    context.PublishOutput(new QueryStreamChunk {
                        Type = AgentStreamChunkType.Content,
                        Content = content
                    });
                }
            }

            // 追踪 Token 使用量 — 消费 provider 统一约定 metadata["Usage"]（TokenUsage JSON，OpenAI/Anthropic 双侧一致）；
            // 旧 InputTokens/OutputTokens 平铺 key 为孤儿读法（全仓无生产者）已删除（TASK063 P7 根因修复）
            if (streamChunk.Metadata?.TryGetValue("Usage", out var usageEl) == true &&
                usageEl.ValueKind == JsonValueKind.Object) {
                try {
                    if (JsonSerializer.Deserialize<JoinCode.Abstractions.LLM.Chat.TokenUsage>(usageEl, Core.Query.QueryJsonContext.Default.TokenUsage) is { } tokenUsage) {
                        inputTokens = tokenUsage.PromptTokens;
                        outputTokens = tokenUsage.CompletionTokens;
                        streamUsage = tokenUsage;
                    }
                } catch (JsonException ex) {
                    _logger?.LogWarning(ex, "[QueryEngine] Usage metadata 反序列化失败，跳过");
                }
            }
        }
        } finally {
            apiStopwatch.Stop();
            context.ApiDurationMs += apiStopwatch.ElapsedMilliseconds;
        }

        // 协议层未识别工具调用 → 文本 fallback 解析（DSML/antml 等）
        if (pendingToolCalls.Count == 0) {
            var fallback = ApplyTextToolCallFallback(responseBuilder, context);
            if (fallback.RequestsAnotherTurn) {
                return new QueryIterationResult {
                    RequestsAnotherTurn = true,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    Usage = streamUsage,
                    FinishReason = lastFinishReason,
                    ModelId = streamModelId ?? context.Options?.ModelId
                };
            }
            pendingToolCalls.AddRange(fallback.Calls);
        }

        if (pendingToolCalls.Count == 0) {
            var finalContent = responseBuilder.ToString();
            if (!string.IsNullOrEmpty(finalContent)) {
                context.ChatHistory.AddAssistantMessage(finalContent);
            }

            return new QueryIterationResult {
                Content = finalContent,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Usage = streamUsage,
                FinishReason = lastFinishReason,
                ModelId = streamModelId ?? context.Options?.ModelId
            };
        }

        // 执行工具调用
        foreach (var tc in pendingToolCalls) {
            _logger?.LogInformation("[QueryEngine] 执行工具调用 #{Num}: {ToolName}", context.TotalToolCalls + 1, tc.ToolName);
        }

        return new QueryIterationResult {
            ToolCalls = pendingToolCalls,
            Content = responseBuilder.ToString(),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Usage = streamUsage,
            FinishReason = lastFinishReason,
            ModelId = streamModelId ?? context.Options?.ModelId
        };
    }

    /// <summary>文本 fallback 解析结果 — 解析出的调用或"已注入纠正提示、需要再来一轮"。</summary>
    private readonly record struct FallbackParseOutcome(List<ToolCallRequest> Calls, bool RequestsAnotherTurn);

    /// <summary>
    /// 协议层无工具调用时尝试文本 fallback — 解析成功则转换为工具调用；
    /// 解析失败且含可疑 XML 标签时注入纠正提示（超上限强制结束），防止循环静默中断。
    /// </summary>
    /// <param name="responseBuilder">累积的响应文本（解析成功后替换为剩余纯文本）。</param>
    /// <param name="context">查询上下文。</param>
    /// <returns>fallback 结果。</returns>
    private FallbackParseOutcome ApplyTextToolCallFallback(StringBuilder responseBuilder, QueryMiddlewareContext context) {
        var parser = _serviceProvider?.GetService<ITextToolCallFallbackParser>();
        var content = responseBuilder.ToString();
        var parsed = parser?.TryParse(content);
        if (parsed is not null && parsed.ToolCalls.Count > 0) {
            context.ConsecutiveMalformedOutput = 0;
            Diag.WriteLine($"[XML-FALLBACK] 文本工具调用解析成功: {parsed.ToolCalls.Count} 个");
            responseBuilder.Clear();
            if (!string.IsNullOrWhiteSpace(parsed.RemainingText))
                responseBuilder.Append(parsed.RemainingText);

            var calls = parsed.ToolCalls
                .Select(call => new ToolCallRequest {
                    ToolName = call.Name,
                    ToolCallId = call.Id,
                    RawArguments = call.Arguments,
                    Arguments = JsonArgumentParser.Parse(call.Arguments)
                })
                .ToList();
            return new FallbackParseOutcome(calls, false);
        }

        if (!OutputInterventionPrompts.ContainsSuspiciousXmlTags(content)) {
            context.ConsecutiveMalformedOutput = 0;
            return new FallbackParseOutcome([], false);
        }

        context.ConsecutiveMalformedOutput++;
        Diag.WriteLine($"[XML-FALLBACK] 可疑标签未能解析: {context.ConsecutiveMalformedOutput}/{OutputInterventionPrompts.MaxConsecutiveMalformed}");
        if (context.ConsecutiveMalformedOutput > OutputInterventionPrompts.MaxConsecutiveMalformed) {
            context.PublishOutput(new QueryStreamChunk {
                Type = AgentStreamChunkType.Content,
                Content = $"⚠ 模型连续{OutputInterventionPrompts.MaxConsecutiveMalformed}次输出异常格式，本轮对话已结束。"
            });
            context.ConsecutiveMalformedOutput = 0;
            return new FallbackParseOutcome([], false);
        }

        context.ChatHistory.AddUserMessage(
            OutputInterventionPrompts.BuildMalformedXmlPrompt(context.ConsecutiveMalformedOutput));
        return new FallbackParseOutcome([], true);
    }

    /// <summary>
    /// 取消时为未完成的工具调用补写 assistant + "(aborted)" 结果 — 保持历史合法
    /// （对齐 chat 循环 WriteAbortedToolResultsAsync；startIndex 起含当前调用）。
    /// </summary>
    /// <param name="context">查询上下文。</param>
    /// <param name="toolCalls">本轮全部工具调用。</param>
    /// <param name="startIndex">起始下标（当前正在执行的调用）。</param>
    /// <param name="assistantContent">assistant 同轮文本（可为 null）。</param>
    private static void WriteAbortedToolResults(
        QueryMiddlewareContext context, IReadOnlyList<ToolCallRequest> toolCalls, int startIndex, string? assistantContent) {
        for (var i = startIndex; i < toolCalls.Count; i++) {
            var toolCall = toolCalls[i];
            var assistantMetadata = toolCall.ToolCallId != null
                ? ToolCallEntry.BuildAssistantMetadata([new() { Id = toolCall.ToolCallId, Name = toolCall.ToolName, Arguments = toolCall.RawArguments ?? "{}" }])
                : new Dictionary<string, JsonElement>();
            context.ChatHistory.Add(new ApiMessage(MessageRole.Assistant, assistantContent, assistantMetadata));
            context.ChatHistory.Add(new ApiMessage(MessageRole.Tool, "(aborted)",
                ToolCallEntry.BuildToolResultMetadata(toolCall.ToolCallId, toolCall.ToolName)));
        }
    }

    /// <summary>
    /// 按剩余调用预算截断本轮工具调用，保留原始下标用于取消时补写 aborted。
    /// </summary>
    private static List<ScheduledToolCall> ScheduleToolCalls(IReadOnlyList<ToolCallRequest> toolCalls, int remainingBudget) {
        var count = Math.Clamp(remainingBudget, 0, toolCalls.Count);
        var scheduled = new List<ScheduledToolCall>(count);
        for (var i = 0; i < count; i++) {
            scheduled.Add(new ScheduledToolCall(toolCalls[i], i));
        }
        return scheduled;
    }

    /// <summary>
    /// 计算从 start 起的批次边界 — 连续并发安全调用同批，非并发安全调用独占（ADR 0160）。
    /// </summary>
    private static async Task<int> DetermineBatchEndAsync(
        List<ScheduledToolCall> scheduled, int start, IToolConcurrencyClassifier? classifier, CancellationToken cancellationToken) {
        var end = start + 1;
        if (classifier is null)
            return end;
        if (!await classifier.IsConcurrencySafeAsync(scheduled[start].Call.ToolName, scheduled[start].Call.Arguments, cancellationToken).ConfigureAwait(false))
            return end;

        while (end < scheduled.Count
            && await classifier.IsConcurrencySafeAsync(scheduled[end].Call.ToolName, scheduled[end].Call.Arguments, cancellationToken).ConfigureAwait(false)) {
            end++;
        }
        return end;
    }

    /// <summary>
    /// 执行工具并把取消（OperationCanceledException + 令牌已取消）映射为 null，
    /// 让批次按原顺序逐条判定 aborted；其他异常已由 <see cref="ExecuteToolAsync"/> 转为错误结果。
    /// </summary>
    private async Task<ToolResult?> TryExecuteToolAsync(ToolCallRequest request, QueryOptions? options, CancellationToken cancellationToken) {
        try {
            return await ExecuteToolAsync(request, options, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return null;
        }
    }

    private async Task<ToolResult> ExecuteToolAsync(ToolCallRequest request, QueryOptions? options, CancellationToken cancellationToken) {
        if (options is not null && !options.IsToolAllowed(request.ToolName)) {
            _logger?.LogWarning("[QueryEngine] 工具被过滤拒绝: {ToolName}", request.ToolName);
            return new ToolResult {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = $"工具 {request.ToolName} 不在当前代理的可用工具列表中" } },
                IsError = true
            };
        }

        try {
            // 工具进度中继（TASK065 P3 — 主代理路径 Bash 执行中输出流式上屏）：SessionController
            // 回合级 SubAgentEventChannel 作用域在场时 Emit 进度事件 → chunk 循环逐拍排空直投消费方；
            // 无作用域（CLI 纯文本/独立调用）null 零影响。registry 兜底路径无 onProgress 形参 — 遗留不发射
            var result = _toolExecutionGateway is not null
                ? await _toolExecutionGateway.ExecuteAsync(
                    request.ToolName, request.Arguments, cancellationToken,
                    JoinCode.Abstractions.LLM.Chat.ToolProgressRelay.Build(request.ToolName)).ConfigureAwait(false)
                : await _toolRegistry.ExecuteToolAsync(request.ToolName, request.Arguments, cancellationToken).ConfigureAwait(false);
            options?.ProgressTracker?.RecordToolUse(request.ToolName);
            return result;
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // 取消不是工具失败 — 向上传播，由调用方补写 (aborted) 结果并终止循环
            throw;
        } catch (Exception ex) {
            _logger?.LogError(ex, "[QueryEngine] 工具调用失败: {ToolName}", request.ToolName);
            options?.ProgressTracker?.RecordToolUse(request.ToolName);
            return new ToolResult {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = $"工具调用失败: {ex.Message}" } },
                IsError = true
            };
        }
    }

    /// <summary>
    /// 工具调用结束事件构造（TASK108 Q7）— 结构化补丁必须随事件下发：
    /// 此前内联构造漏了 <see cref="ToolResult.StructuredPatch"/>，Edit/Write 的 diff 预览在 TUI 永远为空
    /// （FileToolHandlers 填了 patch，但事件不带，下游 <c>ProjectPatch</c> 全链路拿不到）。
    /// </summary>
    internal static QueryStreamChunk BuildToolCallEndChunk(string toolName, string? toolCallId, ToolResult toolResult, int callNumber) => new() {
        Type = AgentStreamChunkType.ToolCallEnd,
        ToolName = toolName,
        ToolCallId = toolCallId,
        ToolResult = toolResult,
        ToolResultText = ExtractToolResultText(toolResult),
        IsToolError = toolResult.IsError,
        StructuredPatch = toolResult.StructuredPatch,
        ToolCallNumber = callNumber,
    };

    /// <summary>
    /// 从 ToolResult 提取纯文本结果 — 错误结果加 "Error: " 前缀，跳过图片等多模态内容
    /// </summary>
    private static string ExtractToolResultText(ToolResult result) {
        var textContents = new List<string>();
        foreach (var c in result.Content) {
            if (result.IsError) {
                if (!string.IsNullOrEmpty(c.Text))
                    textContents.Add($"Error: {c.Text}");
            } else if (!string.IsNullOrEmpty(c.Text)) {
                textContents.Add(c.Text);
            }
        }
        return string.Join("\n", textContents);
    }

    private async ValueTask AddToolResultToHistory(QueryMiddlewareContext context, ToolCallRequest toolCall, ToolResult result, string? assistantContent) {
        // 对齐 TS convertResultContentToContentBlocks — 分离文本和非文本内容
        var textContents = new List<string>();
        var nonTextContents = new List<ToolContent>();

        foreach (var c in result.Content) {
            if (result.IsError) {
                if (!string.IsNullOrEmpty(c.Text))
                    textContents.Add($"Error: {c.Text}");
            } else if (c.Type == ToolContentType.Image && !string.IsNullOrEmpty(c.Data) && !string.IsNullOrEmpty(c.MimeType)) {
                // 图片类型 — 保留为多模态内容块
                nonTextContents.Add(c);
            } else if (!string.IsNullOrEmpty(c.Text)) {
                textContents.Add(c.Text);
            }
            // resource 类型（二进制写盘）已在 McpClientToolHandlers 中转为文本路径
        }

        var toolResultText = string.Join("\n", textContents);

        // 内容替换 — 由 ContentReplacementMiddleware 通过上下文提供 IContentReplacementService
        // MaybePersistLargeToolResult 是即时持久化（非预算机制），在添加历史时调用
        if (context.ContentReplacementService is not null && !result.IsError && !string.IsNullOrEmpty(toolResultText)) {
            var sessionId = context.Options?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
            var replacement = await context.ContentReplacementService.MaybePersistLargeToolResult(
                toolCall.ToolName, toolCall.ToolCallId ?? string.Empty, toolResultText, sessionId).ConfigureAwait(false);
            if (replacement is not null)
                toolResultText = replacement;
        }

        var assistantMetadata = toolCall.ToolCallId != null
            ? ToolCallEntry.BuildAssistantMetadata([new() { Id = toolCall.ToolCallId, Name = toolCall.ToolName, Arguments = toolCall.RawArguments ?? "{}" }])
            : new Dictionary<string, JsonElement>();
        context.ChatHistory.Add(new ApiMessage(MessageRole.Assistant, assistantContent, assistantMetadata));

        var toolMetadata = ToolCallEntry.BuildToolResultMetadata(toolCall.ToolCallId, toolCall.ToolName);
        context.ChatHistory.Add(new ApiMessage(MessageRole.Tool, toolResultText, toolMetadata) {
            // 对齐 TS — 将多模态内容块传递到 ApiMessage，由 ChatService 转换为 LLM API 格式
            ContentBlocks = nonTextContents.Count > 0 ? nonTextContents : []
        });
    }

    private Dictionary<string, JsonElement> ExtractArguments(IReadOnlyDictionary<string, JsonElement>? metadata, string? rawArguments) {
        if (!string.IsNullOrEmpty(rawArguments)) {
            var jsonRepair = LlmJsonHelper.RepairJson(rawArguments, _logger);
            var parsed = JsonArgumentParser.Parse(jsonRepair.Success ? jsonRepair.RepairedJson : rawArguments);
            if (parsed.Count > 0)
                return parsed;

            _logger?.LogWarning("Failed to parse tool call arguments JSON: {Args}", rawArguments);
        }

        if (metadata == null)
            return new Dictionary<string, JsonElement>();

        var fallback = metadata
            .Where(kvp => kvp.Key.StartsWith("Argument_"))
            .ToDictionary(
                kvp => kvp.Key["Argument_".Length..],
                kvp => kvp.Value);
        return fallback;
    }

    /// <summary>
    /// 判断异常是否可重试 — 纯函数（TASK031 阶段2.1 提取为 internal 供测试）
    /// TimeoutException 可重试；HttpRequestException / TaskCanceledException 不可重试
    /// </summary>
    /// <param name="ex">异常</param>
    /// <returns>是否可重试</returns>
    internal static bool IsRetryable(Exception ex) {
        if (Environment.GetEnvironmentVariable(JccEnvVar.DisableRetry.ToValue()) is { } val &&
            (val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1"))
            return false;
        // HttpRequestException 不重试 —
        // ResilientHttpExecutor 已在其层按 ShouldRetry/IsRetryAllowed 完成重试，
        // QueryEngine 再重试只会与底层叠加翻倍等待时间（三层重试叠加问题）
        if (ex is HttpRequestException)
            return false;
        // TaskCanceledException 不重试 —
        // 超时令牌触发后令牌已取消，重试会立即再次取消导致无限重试耗尽预算，
        // 最终返回空回复 rc=1 而非 rc=1240（Bug 29）。
        // SessionController 的 catch(OperationCanceledException) 已区分超时 vs 用户取消。
        if (ex is TaskCanceledException)
            return false;
        return ex is TimeoutException;
    }

    /// <summary>
    /// 计算重试延迟 — 纯函数（TASK031 阶段2.1 提取为 internal 供测试）
    /// 指数退避: delay * 2^(retryCount-1)，上限 MaxDelayMs
    /// </summary>
    /// <param name="retryCount">重试次数（从 1 开始）</param>
    /// <returns>延迟毫秒数</returns>
    internal int CalculateRetryDelay(int retryCount) {
        if (!_config.Retry.EnableExponentialBackoff) {
            return _config.Retry.RetryDelayMs;
        }

        // 指数退避: delay * 2^(retryCount-1)
        var delay = _config.Retry.RetryDelayMs * Math.Pow(2, retryCount - 1);
        return (int)Math.Min(delay, WorkflowConstants.Retry.MaxDelayMs);
    }

    /// <summary>
    /// 重试感知通知（TASK070 E2/DSG046 — 静默重试的感知上屏回调）：context.Options.OnRetry 非 null 时
    /// 触发（主代理路径由 AgentBase.RetryNotifier 透传）；回调异常仅记日志，不影响重试节奏。
    /// </summary>
    internal void NotifyRetry(QueryMiddlewareContext context, string reason, int attempt, int maxRetries, int delayMs) {
        var notifier = context.Options?.OnRetry;
        if (notifier is null) {
            return;
        }

        try {
            notifier(new QueryRetryNotification(reason, attempt, maxRetries, delayMs));
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[QueryEngine] OnRetry 回调异常（忽略，不影响重试）");
        }
    }

    // IQueryEngine 接口实现
    /// <summary>
    /// 执行查询并返回完整字符串结果（阻塞枚举）
    /// </summary>
    /// <param name="query">用户查询</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>完整响应字符串</returns>
    public Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default) {
        var chatHistory = new MessageList();
        var result = new StringBuilder();

        foreach (var chunk in QueryAsync(query, chatHistory, cancellationToken).ToBlockingEnumerable(cancellationToken)) {
            if (chunk.Content != null) {
                result.Append(chunk.Content);
            }
        }

        return Task.FromResult(result.ToString());
    }

    /// <summary>
    /// 获取聊天补全服务
    /// </summary>
    /// <returns>查询服务接口</returns>
    public IQueryService GetChatCompletionService() {
        return _kernel.GetChatCompletionService();
    }

    /// <summary>
    /// 获取 LLM 客户端
    /// </summary>
    /// <returns>聊天客户端</returns>
    public IChatClient GetKernel() {
        return _kernel;
    }

    private JoinCode.Abstractions.LLM.Chat.CacheSafeParams? BuildCacheSafeParams(QueryMiddlewareContext context) {
        var existing = context.Options?.CacheSafeParams;

        return new JoinCode.Abstractions.LLM.Chat.CacheSafeParams {
            RenderedSystemPrompt = existing?.RenderedSystemPrompt,
            ModelId = existing?.ModelId,
            ToolNames = existing?.ToolNames ?? [],
            UserContext = existing is not null
                ? new Dictionary<string, string>(existing.UserContext)
                : [],
            SystemContext = existing is not null
                ? new Dictionary<string, string>(existing.SystemContext)
                : [],
            ContentReplacementState = context.Options?.ContentReplacementState?.Clone()
        };
    }

    /// <summary>
    /// 核心执行中间件 — 管道最内层，执行 LLM 调用 + 工具执行循环
    /// </summary>
    internal sealed class QueryCoreMiddleware : IMiddleware<QueryMiddlewareContext> {
        /// <summary>无状态终点，查询与 profile 重建共用此实例。</summary>
        internal static QueryCoreMiddleware Instance { get; } = new();

        /// <summary>核心委托由每轮查询上下文提供，终点不持有引擎实例。</summary>
        private QueryCoreMiddleware() { }

        /// <summary>
        /// 执行核心循环 — 不调用 next()，作为管道终端
        /// </summary>
        /// <param name="context">中间件上下文</param>
        /// <param name="next">下一委托（不调用）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>表示异步操作的任务</returns>
        public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct) {
            var executeCore = context.ExecuteCoreAsync
                ?? throw new InvalidOperationException("查询管道终点缺少核心执行委托；请从 QueryEngine.QueryAsync 启动查询。");
            return executeCore(context, ct);
        }
    }
}

/// <summary>
/// 已调度的工具调用 — 携带原始下标（取消时从该下标起补写 aborted）。
/// </summary>
internal sealed record ScheduledToolCall(ToolCallRequest Call, int OriginalIndex);

/// <summary>
/// 工具调用请求
/// </summary>
internal sealed class ToolCallRequest {
    /// <summary>获取或设置工具名称</summary>
    public string ToolName { get; set; } = string.Empty;
    /// <summary>获取或设置工具调用 ID</summary>
    public string? ToolCallId { get; set; }
    /// <summary>获取或设置原始参数字符串</summary>
    public string? RawArguments { get; set; }
    /// <summary>获取或设置解析后的参数字典</summary>
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();
}

/// <summary>
/// 迭代执行结果
/// </summary>
internal sealed class QueryIterationResult {
    /// <summary>获取迭代输出内容</summary>
    public string? Content { get; init; }
    /// <summary>获取首个工具调用（存在时）</summary>
    public ToolCallRequest? ToolCall => ToolCalls.Count > 0 ? ToolCalls[0] : null;
    /// <summary>获取本轮所有工具调用列表</summary>
    public List<ToolCallRequest> ToolCalls { get; init; } = new();
    /// <summary>获取输入 Token 数</summary>
    public int InputTokens { get; init; }
    /// <summary>获取输出 Token 数</summary>
    public int OutputTokens { get; init; }
    /// <summary>获取本轮完整 TokenUsage（含缓存命中/创建/推理 token — P7 透传至 Done 事件）</summary>
    public JoinCode.Abstractions.LLM.Chat.TokenUsage? Usage { get; init; }
    /// <summary>获取本轮 LLM finish_reason（stop/length/content_filter/tool_calls），null=未知</summary>
    public string? FinishReason { get; init; }
    /// <summary>获取本轮实际使用的模型标识（从流式 chunk ModelId 提取；null = 未提供）</summary>
    public string? ModelId { get; init; }
    /// <summary>是否需要立即再执行一轮 — 异常输出已注入纠正提示，不进入收尾判定。</summary>
    public bool RequestsAnotherTurn { get; init; }
}

/// <summary>
/// Token 成本追踪接口 — 仅用于 Query 层内部的简单 Token 计数
/// 注意: 与 JoinCode.Abstractions.Interfaces.ICostTracker（完整成本追踪）不同
/// </summary>
public interface ITokenCostTracker {
    /// <summary>
    /// 追踪一次 Token 使用
    /// </summary>
    /// <param name="inputTokens">输入 Token 数</param>
    /// <param name="outputTokens">输出 Token 数</param>
    void TrackUsage(int inputTokens, int outputTokens);

    /// <summary>
    /// 获取总成本（美元）
    /// </summary>
    /// <returns>总成本</returns>
    decimal GetTotalCost();

    /// <summary>
    /// 获取累计使用情况
    /// </summary>
    /// <returns>累计使用快照(输入 Token 数、输出 Token 数、成本)</returns>
    TokenCostUsage GetUsage();
}

/// <summary>
/// 空 Token 成本追踪器
/// </summary>
public partial class NullTokenCostTracker : ITokenCostTracker {
    /// <summary>
    /// 空实现 — 不追踪任何使用
    /// </summary>
    /// <param name="inputTokens">输入 Token 数</param>
    /// <param name="outputTokens">输出 Token 数</param>
    public void TrackUsage(int inputTokens, int outputTokens) { }

    /// <summary>
    /// 返回零成本
    /// </summary>
    /// <returns>零</returns>
    public decimal GetTotalCost() => 0m;

    /// <summary>
    /// 返回零使用
    /// </summary>
    /// <returns>零使用快照</returns>
    public TokenCostUsage GetUsage() => new(0, 0, 0m);
}

/// <summary>
/// Token 成本追踪器实现
/// </summary>
public partial class TokenCostTracker : ITokenCostTracker {
    private readonly CostTrackingConfig _config;
    private int _totalInputTokens;
    private int _totalOutputTokens;

    /// <summary>
    /// 构造函数 — 注入成本追踪配置
    /// </summary>
    /// <param name="config">成本追踪配置</param>
    public TokenCostTracker(CostTrackingConfig config) {
        _config = config;
    }

    /// <summary>
    /// 追踪一次 Token 使用 — 累加到总计数
    /// </summary>
    /// <param name="inputTokens">输入 Token 数</param>
    /// <param name="outputTokens">输出 Token 数</param>
    public void TrackUsage(int inputTokens, int outputTokens) {
        if (!_config.Enabled) return;

        Interlocked.Add(ref _totalInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
    }

    /// <summary>
    /// 获取总成本（美元）— 按输入/输出单价计算
    /// </summary>
    /// <returns>总成本</returns>
    public decimal GetTotalCost() {
        if (!_config.Enabled) return 0m;

        var inputCost = (_totalInputTokens / 1000m) * _config.InputTokenCostPer1K;
        var outputCost = (_totalOutputTokens / 1000m) * _config.OutputTokenCostPer1K;
        return inputCost + outputCost;
    }

    /// <summary>
    /// 获取累计使用情况
    /// </summary>
    /// <returns>累计使用快照(输入 Token 数、输出 Token 数、成本)</returns>
    public TokenCostUsage GetUsage() {
        var cost = GetTotalCost();
        return new TokenCostUsage(_totalInputTokens, _totalOutputTokens, cost);
    }
}

/// <summary>
/// Token 累计使用快照 — 输入 Token 数、输出 Token 数、累计成本
/// </summary>
/// <param name="InputTokens">累计输入 Token 数</param>
/// <param name="OutputTokens">累计输出 Token 数</param>
/// <param name="Cost">累计成本(美元)</param>
public sealed record TokenCostUsage(int InputTokens, int OutputTokens, decimal Cost);