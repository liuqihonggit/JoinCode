namespace JoinCode.Adapters;

/// <summary>
/// 传输无关的会话驱动器 — 统一事件消费逻辑。
/// 从 CliSession 的 StreamResponseAsync 中提取共享逻辑。
/// </summary>
public sealed class SessionController {
    private readonly IChatService _chatService;
    private readonly IEventConsumer _consumer;
    private readonly TurnDiffService _turnDiffService;

    /// <summary>本回合是否有实质产出（空回合防御判定 — 工具调用/子代理事件置位、回合开始复位；文本/思考经长度判定不入此标记）。</summary>
    private bool _hasSubstantiveOutput;
    /// <summary>本回合 Done 权威 usage 快照（TASK070 C4 — 自动压缩阈值判定的 token 数据源，
    /// 与 EmitContextUsage 同源；回合开始复位防跨回合泄漏）。</summary>
    private TokenUsage? _lastDoneUsage;
    /// <summary>最近一次自动压缩后台任务（TASK070 — internal 测试观察点：单测 await 它消除 fire-and-forget 时序不确定性）。</summary>
    internal Task? LastAutoCompactTask { get; private set; }
    private readonly string _sessionId;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IClockService _clock;
    private readonly AgentBase? _mainAgent;

    /// <summary>会话是否正在运行</summary>
    public bool IsRunning { get; private set; } = true;

    /// <summary>最后一次响应文本</summary>
    public string LastResponse { get; private set; } = string.Empty;

    /// <summary>聊天服务</summary>
    public IChatService ChatService => _chatService;

    /// <summary>
    /// 构造函数 — 注入聊天服务、事件消费者、轮次差异服务及可选依赖
    /// </summary>
    /// <param name="chatService">聊天服务，用于流式事件推送</param>
    /// <param name="consumer">事件消费者，接收文本/工具/思考等事件</param>
    /// <param name="turnDiffService">轮次差异服务，记录文件编辑用于差异展示</param>
    /// <param name="sessionId">会话标识，用于思考内容持久化与崩溃快照</param>
    /// <param name="serviceProvider">服务提供者，可选，用于解析主代理路径依赖</param>
    /// <param name="clock">时钟服务，可选，默认使用系统时钟</param>
    /// <param name="mainAgent">主代理实例，可选，非空时走主代理执行路径</param>
    public SessionController(
        IChatService chatService,
        IEventConsumer consumer,
        TurnDiffService turnDiffService,
        string sessionId,
        IServiceProvider? serviceProvider = null,
        IClockService? clock = null,
        AgentBase? mainAgent = null) {
        _chatService = chatService;
        _consumer = consumer;
        _turnDiffService = turnDiffService;
        _sessionId = sessionId;
        _serviceProvider = serviceProvider;
        _clock = clock ?? SystemClockService.Instance;
        _mainAgent = mainAgent;

        // TASK070 E2/DSG046 — 重试感知绑定：主代理 BuildChatOptions → QueryOptions.OnRetry →
        // QueryEngine 指数退避重试前回调 → consumer.OnRetry 上屏（未注入 notifier = 静默重试旧行为）。
        if (_mainAgent is not null) {
            _mainAgent.RetryNotifier = notification =>
                _consumer.OnRetry(notification.Reason, notification.Attempt, notification.MaxRetries, notification.DelayMs);
        }
    }

    /// <summary>
    /// 停止会话
    /// </summary>
    public void Stop() => IsRunning = false;

    /// <summary>
    /// 流式处理用户输入 — 统一的事件消费逻辑
    /// </summary>
    public async Task<SessionTurnResult> StreamResponseAsync(string input, CancellationToken cancellationToken) {
        // 发送提醒注入 — system-reminder 形态追加到消息末尾（10 分钟冷却；斜杠命令在 EngineSession 层已分流，
        // 此处兜底判定）。CurrentInput（LLM prompt）与 AddUserMessageAsync（历史落库）同源生效。
        input = SendReminderInjector.Instance.Append(input);

        if (_consumer is IResettableEventConsumer resettable)
            resettable.Reset();

        var fullResponse = new StringBuilder();
        var thinkingContent = new StringBuilder();
        var lastModelId = (string?)null;
        var requestTimestamp = _clock.GetUtcNow();

        var apiTimeoutMs = ParseApiTimeoutMs();
        LogApiTimeoutOnce(apiTimeoutMs);
        using var timeoutCts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromMilliseconds(apiTimeoutMs));
        var timeoutToken = timeoutCts.Token;
        var hasReceivedEvent = false;
        _hasSubstantiveOutput = false;
        _lastDoneUsage = null;

        try {
            if (_mainAgent is not null) {
                var contextManagerForCount = _serviceProvider?.GetService<IChatContextManager>();
                var startCount = contextManagerForCount?.CurrentMessageCount ?? 0;
                var preprocess = await PreProcessMainAgentAsync(input, timeoutToken).ConfigureAwait(false);
                if (preprocess.PromptInjection is { Length: > 0 } injection) {
                    _consumer.OnText(injection + "\n\n");
                }
                if (preprocess.ModalityInjection is { Length: > 0 } modalityInjection) {
                    _consumer.OnText(modalityInjection + "\n");
                }

                var auditLogger = _serviceProvider?.GetService<ILogger<SessionController>>();
                auditLogger?.LogInformation("[Audit] User: {Message}", input.Length > 200 ? string.Concat(input.AsSpan(0, 200), "...") : input);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _mainAgent.CurrentInput = input;
                // DSG041 批2 — 子代理事件通道：主代理路径回合级作用域（对齐 QueryLoopMiddleware 先例 —
                // 作用域覆盖 QueryEngine 工具执行段，Agent 工具中间件 Emit 的子代理事件进通道；
                // chunk 循环逐拍排空直投 ProcessEvent[批1 分流已就绪]，不经 chunk 转换层）
                var subAgentChannel = new SubAgentEventChannel();
                using (var subAgentScope = subAgentChannel.EnterScope()) {
                    await foreach (var chunk in _mainAgent.ExecuteStreamAsync(timeoutToken).ConfigureAwait(false)) {
                        hasReceivedEvent = true;
                        DrainSubAgentEvents(subAgentChannel, fullResponse, thinkingContent);
                        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);
                        if (evt is null) continue;
                        var mid = ProcessEvent(evt, fullResponse, thinkingContent);
                        if (mid is not null) lastModelId = mid;
                    }
                }

                // 末批冲刷（循环结束后通道可能仍有缓冲事件）+ 完成标记（迟到 Emit 静默丢弃防泄漏）
                DrainSubAgentEvents(subAgentChannel, fullResponse, thinkingContent);
                subAgentChannel.Complete();
                sw.Stop();

                if (ShouldShowTurnDuration() || Diag.IsDebugLog) {
                    _consumer.OnTimingSummary($"Total: {sw.ElapsedMilliseconds}ms");
                }

                auditLogger?.LogInformation("[Audit] Assistant: {Chars} chars, Model={Model}", fullResponse.Length, lastModelId);

                await PostProcessMainAgentAsync(preprocess.PreprocessResult, fullResponse.ToString(), startCount, lastModelId, cancellationToken).ConfigureAwait(false);
            } else {
                await foreach (var evt in _chatService.StreamWithEventsAsync(input, timeoutToken).ConfigureAwait(false)) {
                    hasReceivedEvent = true;
                    var mid = ProcessEvent(evt, fullResponse, thinkingContent);
                    if (mid is not null) lastModelId = mid;
                }
            }

            LastResponse = fullResponse.ToString();

            // 等待思考内容持久化完成，避免 fire-and-forget 丢失
            await StoreThinkingIfAnyAsync(thinkingContent, lastModelId, cancellationToken).ConfigureAwait(false);

            // TASK070 C4/DSG046 — 回合终态自动压缩判定（对齐 TS autoCompact.ts:241-351 回合末触发）：
            // Done 权威 usage + ICompactService.ShouldAutoCompact 阈值命中时，压缩异步执行不阻塞 idle 返回。
            TryTriggerAutoCompact(_lastDoneUsage);

            // 空回合防御（修复：流式超时重试后拿到空流被当成功静默返回 — TUI 傻等无消息无错误）：
            // 无文本 + 无思考 + 无工具调用 = 全空产出，判 Error 带诱导（重试提示）；
            // 工具回合/思考回合文本空属合法半产出（onToolEnd/onThinking 已独立消费），不误判。
            if (LastResponse.Length == 0 && thinkingContent.Length == 0 && !_hasSubstantiveOutput) {
                return SessionTurnResult.Error(
                    "模型返回空回复（无文本/思考/工具产出）。可能为请求过大超时或供应商响应异常，请按 Esc 打断后重试，或切换模型/供应商。",
                    partialResponse: string.Empty);
            }

            return SessionTurnResult.Success(LastResponse, requestTimestamp);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !hasReceivedEvent) {
            await StoreThinkingIfAnyAsync(thinkingContent, lastModelId, CancellationToken.None).ConfigureAwait(false);
            return SessionTurnResult.Timeout(apiTimeoutMs);
        } catch (OperationCanceledException) {
            LastResponse = fullResponse.ToString();
            await StoreThinkingIfAnyAsync(thinkingContent, lastModelId, CancellationToken.None).ConfigureAwait(false);
            return SessionTurnResult.FromCancellation(LastResponse);
        } catch (Exception ex) {
            LastResponse = fullResponse.ToString();
            await StoreThinkingIfAnyAsync(thinkingContent, lastModelId, CancellationToken.None).ConfigureAwait(false);

            var crashStore = _serviceProvider?.GetService<ICrashSnapshotStore>();
            if (crashStore is not null && ex is not OperationCanceledException) {
                try {
                    crashStore.Add(new CrashSnapshot(
                        fenceName: "MainAgent",
                        severity: CrashSeverity.Error,
                        exception: ex,
                        executionContext: new CrashExecutionContext { SessionId = _sessionId }));
                } catch (Exception snapshotEx) {
                    _serviceProvider?.GetService<ILogger<SessionController>>()?.LogWarning(snapshotEx, "[SessionController] 崩溃快照保存失败");
                }
            }

            if (ex is JoinCode.Abstractions.Exceptions.ApiException apiEx)
                return SessionTurnResult.Error(apiEx.Message, LastResponse, apiEx.ErrorCode, apiEx.IsRetryable);
            if (ex is JoinCode.Abstractions.Exceptions.WorkflowException wfEx)
                return SessionTurnResult.Error(wfEx.Message, LastResponse, wfEx.ErrorCode);
            return SessionTurnResult.Error(ex.Message, LastResponse);
        }
    }

    /// <summary>
    /// 排空子代理事件通道并直投 ProcessEvent（DSG041 批2 — chunk 循环逐拍调用；
    /// 子代理事件带 AgentId 走 RouteSubagentEvent 分流，主对话零影响）
    /// </summary>
    private void DrainSubAgentEvents(SubAgentEventChannel channel, StringBuilder fullResponse, StringBuilder thinkingContent) {
        foreach (var subEvent in channel.TryDrain()) {
            ProcessEvent(subEvent, fullResponse, thinkingContent);
        }
    }

    /// <summary>
    /// 处理单个 ChatStreamEvent — 子代理活动事件（AgentId 非 null）前置分流到子代理回调（DSG041 批1），
    /// 其余分发到 IEventConsumer 既有回调，返回 Done 事件的 modelId（否则 null）
    /// </summary>
    private string? ProcessEvent(ChatStreamEvent evt, StringBuilder fullResponse, StringBuilder thinkingContent) {
        string? modelId = null;
        if (RouteSubagentEvent(evt)) {
            _hasSubstantiveOutput = true;
            return null;
        }

        evt.Switch(
            onText: content => {
                if (content.Length > 0) fullResponse.Append(content);
                _consumer.OnText(content);
            },
            onThinking: thinking => {
                if (thinking.Length > 0) thinkingContent.Append(thinking);
                _consumer.OnThinking(thinking);
            },
            onToolStart: (toolName, callId, arguments) => {
                _hasSubstantiveOutput = true;
                _consumer.OnToolStart(toolName, callId, arguments);
            },
            onToolEnd: (toolName, resultText, callId, isToolError, structuredPatch) => {
                _consumer.OnToolEnd(toolName, resultText, callId, isToolError, structuredPatch);
                RecordToolCallForTurnDiff(toolName, resultText, structuredPatch);
            },
            onToolProgress: (toolName, progressType, progressMessage) => {
                _consumer.OnToolProgress(toolName, progressType, progressMessage);
            },
            onLoopDetected: (triggerCount, loopStartIndex, repeatedPattern) => {
                _consumer.OnLoopDetected(triggerCount, loopStartIndex, repeatedPattern);
            },
            onTimingSummary: summary => {
                _consumer.OnTimingSummary(summary);
            },
            onDone: (usage, mid) => {
                modelId = mid;
                _consumer.OnDone(usage, mid);
                EmitContextUsage(usage);
                if (usage is { PromptTokens: > 0 })
                    _lastDoneUsage = usage;
            });
        return modelId;
    }

    /// <summary>缺省上下文窗口兜底（对齐 ChatContextManager.DefaultContextWindowResolver 的 200K）。</summary>
    private const int DefaultContextWindowTokens = 200_000;

    /// <summary>
    /// 回合耗时显示判定 — WorkflowConfig.ShowTurnDuration（对齐 TS 默认 true）；
    /// sp 不可用或未注册 WorkflowConfig 时默认 true，debuglog 开启时强制显示。
    /// </summary>
    private bool ShouldShowTurnDuration() =>
        _serviceProvider?.GetService<WorkflowConfig>()?.ShowTurnDuration ?? true;

    /// <summary>
    /// 发射上下文余量事件（M8-7 — Done 权威 usage 快照口径）：PromptTokens 即当前上下文窗口占用；
    /// 窗口优先 IContextWindowResolver（不可达兜底 200K），percentLeft 走 ICompactService.CalculateWarningState
    /// 权威阈值算法（不可达时朴素窗口百分比兜底）→ _consumer.OnContextUsage。
    /// usage 为 null 或 PromptTokens=0（mock/无计量路径）不发射，防消费侧噪音。
    /// </summary>
    private void EmitContextUsage(TokenUsage? usage) {
        if (usage is not { PromptTokens: > 0 })
            return;

        var promptTokens = usage.PromptTokens;
        var contextWindowTokens = _serviceProvider?.GetService<IContextWindowResolver>()?.ResolveCurrentContextWindow()
            ?? DefaultContextWindowTokens;
        var percentLeft = _serviceProvider?.GetService<ICompactService>() is { } compact
            ? compact.CalculateWarningState(promptTokens, contextWindowTokens).PercentLeft
            : Math.Max(0, (int)Math.Round((contextWindowTokens - promptTokens) * 100.0 / contextWindowTokens));
        _consumer.OnContextUsage(promptTokens, contextWindowTokens, percentLeft);
    }

    /// <summary>
    /// 自动压缩判定与触发（TASK070 C4/DSG046 — AutoCompactService.ShouldAutoCompact 首个生产调用方）：
    /// usage 缺失 / ICompactService 不可达 / 阈值未达 三重门控静默跳过；命中后压缩后台异步执行
    /// （LLM 摘要含网络调用，不阻塞回合终态返回 — 对齐 DSG046"CompactAsync 异步防阻塞 idle"）。
    /// </summary>
    private void TryTriggerAutoCompact(TokenUsage? usage) {
        if (usage is not { PromptTokens: > 0 })
            return;

        var compact = _serviceProvider?.GetService<ICompactService>();
        if (compact is null)
            return;

        var contextWindowTokens = _serviceProvider?.GetService<IContextWindowResolver>()?.ResolveCurrentContextWindow()
            ?? DefaultContextWindowTokens;
        if (!compact.ShouldAutoCompact(usage.PromptTokens, contextWindowTokens))
            return;

        LastAutoCompactTask = RunAutoCompactAsync(usage.PromptTokens);
    }

    /// <summary>
    /// 后台执行自动压缩 — 摘要生成（CompactPromptTemplate 结构化 9 段式，与 CompactCommand 手动路径同链）
    /// → CompactHistoryAsync 历史压实 → OnCompactCompleted(isAuto:true) 事件上屏；失败仅记日志（回合已成功，不回滚）。
    /// </summary>
    private async Task RunAutoCompactAsync(int promptTokens) {
        try {
            var history = await _chatService.GetMessageListAsync(CancellationToken.None).ConfigureAwait(false);
            if (history.Count == 0)
                return;

            var compactPrompt = Core.Prompts.Templates.Memory.CompactPromptTemplate.GetCompactPrompt(customInstructions: null);
            var rawSummary = await _chatService.SendMessageAsync(compactPrompt, CancellationToken.None).ConfigureAwait(false);
            var summary = Core.Prompts.Templates.Memory.CompactPromptTemplate.FormatCompactSummary(rawSummary ?? "[无摘要]");
            var compressedTokens = summary.Length / 4;

            await _chatService.CompactHistoryAsync(summary, CancellationToken.None).ConfigureAwait(false);

            _consumer.OnCompactCompleted(history.Count, promptTokens, compressedTokens, isAuto: true);
            _serviceProvider?.GetService<ILogger<SessionController>>()?.LogInformation(
                "[SessionController] 自动压缩完成: {Messages} 条消息, {Original} → {Compressed} tokens",
                history.Count, promptTokens, compressedTokens);
        } catch (Exception ex) {
            _serviceProvider?.GetService<ILogger<SessionController>>()?.LogWarning(ex, "[SessionController] 自动压缩失败（回合产出不受影响）");
        }
    }

    /// <summary>
    /// 子代理活动事件前置分流（DSG041 批1 — AgentId 非 null 即子代理事件，路由到三子代理回调）：
    /// AgentStarted → OnSubagentStarted（身份三元组）、AgentFinished → OnSubagentFinished（成败）、
    /// 带 AgentId 的工具事件 → OnSubagentToolUse（工具名 + 摘要；tokenGain 批2 通道接通后填充）。
    /// 返回 true = 已消费（主 Switch 跳过）；false = 主对话事件走既有路径。
    /// </summary>
    private bool RouteSubagentEvent(ChatStreamEvent evt) {
        if (evt.AgentId is not { } agentId) {
            return false;
        }

        switch (evt.Type) {
            case ChatStreamEventType.AgentStarted:
                _consumer.OnSubagentStarted(agentId, evt.AgentName ?? string.Empty, evt.AgentDescription ?? string.Empty);
                return true;
            case ChatStreamEventType.AgentFinished:
                _consumer.OnSubagentFinished(agentId, isError: evt.AgentSuccess is not true, tokenTotal: evt.Usage?.TotalTokens ?? 0);
                return true;
            case ChatStreamEventType.ToolCallStart:
                _consumer.OnSubagentToolUse(agentId, evt.ToolName ?? string.Empty, evt.ToolArguments ?? string.Empty, tokenGain: 0);
                return true;
            case ChatStreamEventType.ToolCallEnd:
                _consumer.OnSubagentToolUse(agentId, evt.ToolName ?? string.Empty, evt.ToolResultText ?? string.Empty, tokenGain: 0);
                return true;
            case ChatStreamEventType.ToolProgress:
                _consumer.OnSubagentToolUse(agentId, evt.ToolName ?? string.Empty, evt.ProgressMessage ?? string.Empty, tokenGain: 0);
                return true;
            default:
                // 子代理的其他事件类型（Content/Thinking 等）暂不投影 — 消费面仅三回调
                return false;
        }
    }

    /// <summary>
    /// 主代理预处理结果 — 包含预处理结果和注入文本
    /// </summary>
    private sealed record MainAgentPreprocess(
        PreprocessResult? PreprocessResult,
        string? PromptInjection,
        string? ModalityInjection);

    /// <summary>
    /// 主代理路径预处理 — 对齐 PreChatMiddleware + ModalityValidationMiddleware：
    /// 文件上下文 + prompt injection + context 准备 + prompt 状态记录 + 模态验证
    /// </summary>
    private async Task<MainAgentPreprocess> PreProcessMainAgentAsync(string input, CancellationToken ct) {
        if (_serviceProvider is null) return new MainAgentPreprocess(null, null, null);

        var fileContextService = _serviceProvider.GetService<IChatFileContextService>();
        fileContextService?.UpdateFileContext(input);

        var preprocessor = _serviceProvider.GetService<IChatPreprocessor>();
        PreprocessResult? preprocessResult = null;
        if (preprocessor is not null) {
            preprocessResult = await preprocessor.AnalyzeAndInjectAsync(input, ct).ConfigureAwait(false);
            await preprocessor.PrepareContextAsync(input, false, ct).ConfigureAwait(false);
        }

        var contextManager = _serviceProvider.GetService<IChatContextManager>();
        if (contextManager is not null) {
            await contextManager.RecordPromptStateAsync(cancellationToken: ct).ConfigureAwait(false);
        }

        var modalityInjection = DetectModalityMismatch(input);

        return new MainAgentPreprocess(
            preprocessResult,
            preprocessResult?.PromptInjectionInfo,
            modalityInjection);
    }

    /// <summary>
    /// 检测模态不匹配 — 对齐 ModalityValidationMiddleware
    /// </summary>
    private string? DetectModalityMismatch(string input) {
        if (_serviceProvider is null) return null;

        var modelConfigLoader = _serviceProvider.GetService<IModelConfigLoader>();
        var workflowConfig = _serviceProvider.GetService<WorkflowConfig>();
        if (modelConfigLoader is null || workflowConfig is null) return null;

        var detector = new MediaIntentDetector();
        var detection = detector.Detect(input);
        if (detection.DetectedModalities == ModelModalityKind.None) return null;

        var vendor = workflowConfig.Provider.Vendor;
        var modelId = workflowConfig.Provider.ModelId;
        var modelModalities = modelConfigLoader.GetModalities(vendor, modelId);

        var missing = detection.DetectedModalities & ~modelModalities;
        if (missing == ModelModalityKind.None) return null;

        var missingDesc = ModalityMismatchMessageBuilder.FormatMissingModalities(missing);
        var keywordsDesc = string.Join(", ", detection.MatchedKeywords);
        return ModalityMismatchMessageBuilder.Build(modelId, missing, missingDesc, keywordsDesc);
    }

    /// <summary>
    /// 主代理路径后处理 — 对齐 SaveContextMiddleware + CleanupInjectionsMiddleware：
    /// 回写 assistant 响应 → 持久化上下文 → 清理注入
    /// </summary>
    private async Task PostProcessMainAgentAsync(PreprocessResult? preprocessResult, string assistantResponse, int startCount, string? modelId, CancellationToken ct) {
        if (_serviceProvider is null) return;

        var contextManager = _serviceProvider.GetService<IChatContextManager>();
        if (contextManager is not null) {
            try {
                if (!string.IsNullOrEmpty(assistantResponse)) {
                    await contextManager.AddAssistantMessageAsync(assistantResponse, ct).ConfigureAwait(false);
                }
                await contextManager.SaveContextAsync(ct).ConfigureAwait(false);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                _serviceProvider.GetService<ILogger<SessionController>>()?.LogError(ex, "[SessionController] 上下文保存失败");
            }
        }

        if (preprocessResult is not null) {
            var preprocessor = _serviceProvider.GetService<IChatPreprocessor>();
            if (preprocessor is not null) {
                try {
                    await preprocessor.CleanupInjectionsAsync(
                        preprocessResult.KeywordResult,
                        preprocessResult.SynonymInjectionIds, ct).ConfigureAwait(false);
                } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    throw;
                } catch (Exception ex) {
                    _serviceProvider.GetService<ILogger<SessionController>>()?.LogError(ex, "[SessionController] 清理注入失败");
                }
            }
        }

        // transcript 增量持久化 — 对齐 TranscriptPersistMiddleware（mainAgent 路径绕过 ChatService 管道，需手动补；
        // 取快照差量 → 过滤空 content → AppendEntriesAsync，含 Worker 进程守卫）
        if (contextManager is null)
            return;
        var transcriptService = _serviceProvider.GetService<ITranscriptService>();
        if (transcriptService is null)
            return;
        var agentRole = Environment.GetEnvironmentVariable(JccEnvVar.AgentRole.ToValue());
        if (string.Equals(agentRole, "worker", StringComparison.OrdinalIgnoreCase))
            return;
        try {
            var snapshot = await contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
            if (snapshot.Count <= startCount)
                return;
            var entries = new List<TranscriptEntry>(snapshot.Count - startCount);
            for (var i = startCount; i < snapshot.Count; i++) {
                var message = snapshot[i];
                if (string.IsNullOrEmpty(message.Content))
                    continue;
                entries.Add(new TranscriptEntry {
                    Role = message.Role.ToValue(),
                    Content = message.Content,
                    Timestamp = DateTime.UtcNow,
                    ModelId = modelId,
                });
            }
            if (entries.Count > 0) {
                await transcriptService.AppendEntriesAsync(contextManager.SessionId, entries, ct).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            _serviceProvider.GetService<ILogger<SessionController>>()?.LogError(ex, "[SessionController] transcript 写入失败（会话 {SessionId}）", contextManager.SessionId);
        }
    }

    /// <summary>
    /// 持久化思考内容 — 等待完成并记录失败，避免 fire-and-forget 静默丢失
    /// </summary>
    private async Task StoreThinkingIfAnyAsync(StringBuilder thinkingContent, string? modelId, CancellationToken ct) {
        if (thinkingContent.Length == 0) return;
        var thinkingStore = _serviceProvider?.GetService<IThinkingStore>();
        if (thinkingStore is null) return;
        try {
            await thinkingStore.StoreAsync(_sessionId, thinkingContent.ToString(), modelId, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            var logger = _serviceProvider?.GetService<ILogger<SessionController>>();
            logger?.LogError(ex, "[SessionController] 思考内容持久化失败");
        }
    }

    private static int ParseApiTimeoutMs() {
        var env = Environment.GetEnvironmentVariable("JCC_API_TIMEOUT_MS");
        if (int.TryParse(env, out var ms) && ms > 0)
            return ms;
        // 默认 120s（修复：原 10s 对慢推理模型过短 — 巨型请求时代曾在重试后被掐致空回复；
        // 推理型/长思考模型首包常 > 10s，120s 对齐主流 CLI 惯例，
        // 超时兜底已有空回合防御 + Timeout 诱导呈现，不怕过长等待）
        return 120_000;
    }

    private static int _apiTimeoutLogged;
    private void LogApiTimeoutOnce(int apiTimeoutMs) {
        if (Interlocked.Exchange(ref _apiTimeoutLogged, 1) != 0) return;
        var logger = _serviceProvider?.GetService<ILogger<SessionController>>();
        logger?.LogDebug("[SessionController] API timeout: {Ms}ms (JCC_API_TIMEOUT_MS={Env})", apiTimeoutMs, Environment.GetEnvironmentVariable("JCC_API_TIMEOUT_MS") ?? "(未设置)");
    }

    private void RecordToolCallForTurnDiff(string toolName, string? resultText, StructuredPatchHunk[]? structuredPatch) {
        var isFileEdit = toolName is FileToolNameEnumConstants.FileWrite or FileToolNameEnumConstants.FileEdit
            or FileToolNameEnumConstants.FileEditRegex or FileToolNameEnumConstants.FileBatchEdit
            or FileToolNameEnumConstants.FileInsertLines or FileToolNameEnumConstants.FileDeleteLines;
        if (!isFileEdit) return;

        var filePath = ExtractFilePathFromResult(resultText);
        if (filePath is null) return;
        var isNewFile = toolName == FileToolNameEnumConstants.FileWrite;

        if (structuredPatch is not null && structuredPatch.Length > 0)
            _turnDiffService.RecordFileEditWithPatch(filePath, structuredPatch, isNewFile);
        else
            _turnDiffService.RecordFileEdit(filePath, resultText, isNewFile);
    }

    private static string? ExtractFilePathFromResult(string? resultText) {
        if (string.IsNullOrWhiteSpace(resultText)) return null;
        foreach (var line in resultText.AsSpan().EnumerateLines()) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("File:")) return trimmed.Slice(5).Trim().ToString();
            if (trimmed.StartsWith("filePath:")) return trimmed.Slice(9).Trim().ToString();
        }
        var firstLine = resultText.AsSpan();
        var newlineIdx = firstLine.IndexOf('\n');
        if (newlineIdx > 0) firstLine = firstLine.Slice(0, newlineIdx);
        firstLine = firstLine.Trim();
        if (firstLine.Length > 0 && (firstLine.Contains('/') || firstLine.Contains('\\') || firstLine.EndsWith(".cs")))
            return firstLine.ToString();
        return null;
    }
}

/// <summary>
/// 会话轮次结果
/// </summary>
public sealed class SessionTurnResult {
    /// <summary>是否成功完成</summary>
    public bool Succeeded { get; init; }
    /// <summary>是否因超时而终止</summary>
    public bool TimedOut { get; init; }
    /// <summary>是否因用户取消而终止</summary>
    public bool WasCancelled { get; init; }
    /// <summary>超时毫秒数（仅 TimedOut 时有效）</summary>
    public int TimeoutMs { get; init; }
    /// <summary>错误消息（仅失败时有效）</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>错误代码（仅失败时有效）</summary>
    public string? ErrorCode { get; init; }
    /// <summary>错误是否可重试</summary>
    public bool IsRetryable { get; init; }
    /// <summary>响应文本（成功为完整响应，失败为部分响应）</summary>
    public string Response { get; init; } = string.Empty;
    /// <summary>请求时间戳</summary>
    public DateTime RequestTimestamp { get; init; }

    /// <summary>构造成功结果</summary>
    /// <param name="response">完整响应文本</param>
    /// <param name="requestTimestamp">请求时间戳</param>
    /// <returns>成功的会话轮次结果</returns>
    public static SessionTurnResult Success(string response, DateTime requestTimestamp) => new() {
        Succeeded = true,
        Response = response,
        RequestTimestamp = requestTimestamp
    };

    /// <summary>构造超时结果</summary>
    /// <param name="timeoutMs">超时毫秒数</param>
    /// <returns>超时的会话轮次结果</returns>
    public static SessionTurnResult Timeout(int timeoutMs) => new() {
        TimedOut = true,
        TimeoutMs = timeoutMs
    };

    /// <summary>构造取消结果</summary>
    /// <param name="partialResponse">取消前的部分响应文本</param>
    /// <returns>已取消的会话轮次结果</returns>
    public static SessionTurnResult FromCancellation(string partialResponse) => new() {
        WasCancelled = true,
        Response = partialResponse
    };

    /// <summary>构造错误结果</summary>
    /// <param name="errorMessage">错误消息</param>
    /// <param name="partialResponse">错误前的部分响应文本</param>
    /// <param name="errorCode">错误代码，可选</param>
    /// <param name="isRetryable">错误是否可重试，默认 false</param>
    /// <returns>错误的会话轮次结果</returns>
    public static SessionTurnResult Error(string errorMessage, string partialResponse, string? errorCode = null, bool isRetryable = false) => new() {
        ErrorMessage = errorMessage,
        ErrorCode = errorCode,
        IsRetryable = isRetryable,
        Response = partialResponse
    };
}