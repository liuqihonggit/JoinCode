namespace JoinCode.Adapters;

/// <summary>
/// 传输无关的会话驱动器 — 统一事件消费逻辑
/// 从 CliSession/TuiSession 的 StreamResponseAsync 中提取共享逻辑
/// </summary>
public sealed class SessionController
{
    private readonly IChatService _chatService;
    private readonly IEventConsumer _consumer;
    private readonly TurnDiffService _turnDiffService;
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

    public SessionController(
        IChatService chatService,
        IEventConsumer consumer,
        TurnDiffService turnDiffService,
        string sessionId,
        IServiceProvider? serviceProvider = null,
        IClockService? clock = null,
        AgentBase? mainAgent = null)
    {
        _chatService = chatService;
        _consumer = consumer;
        _turnDiffService = turnDiffService;
        _sessionId = sessionId;
        _serviceProvider = serviceProvider;
        _clock = clock ?? SystemClockService.Instance;
        _mainAgent = mainAgent;
    }

    /// <summary>
    /// 停止会话
    /// </summary>
    public void Stop() => IsRunning = false;

    /// <summary>
    /// 流式处理用户输入 — 统一的事件消费逻辑
    /// PermissionPendingConfirmationException 不会被捕获，会向上传播供调用方处理
    /// </summary>
    public async Task<SessionTurnResult> StreamResponseAsync(string input, CancellationToken cancellationToken)
    {
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

        try
        {
            if (_mainAgent is not null)
            {
                var preprocess = await PreProcessMainAgentAsync(input, timeoutToken).ConfigureAwait(false);
                if (preprocess.PromptInjection is { Length: > 0 } injection)
                {
                    _consumer.OnText(injection + "\n\n");
                }
                if (preprocess.ModalityInjection is { Length: > 0 } modalityInjection)
                {
                    _consumer.OnText(modalityInjection + "\n");
                }

                var auditLogger = _serviceProvider?.GetService<ILogger<SessionController>>();
                auditLogger?.LogInformation("[Audit] User: {Message}", input.Length > 200 ? string.Concat(input.AsSpan(0, 200), "...") : input);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _mainAgent.CurrentInput = input;
                await foreach (var chunk in _mainAgent.ExecuteStreamAsync(timeoutToken).ConfigureAwait(false))
                {
                    hasReceivedEvent = true;
                    var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);
                    if (evt is null) continue;
                    var mid = ProcessEvent(evt, fullResponse, thinkingContent);
                    if (mid is not null) lastModelId = mid;
                }
                sw.Stop();

                if (Diag.IsDebugLog)
                {
                    _consumer.OnTimingSummary($"Total: {sw.ElapsedMilliseconds}ms");
                }

                auditLogger?.LogInformation("[Audit] Assistant: {Chars} chars, Model={Model}", fullResponse.Length, lastModelId);

                await PostProcessMainAgentAsync(preprocess.PreprocessResult, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await foreach (var evt in _chatService.StreamWithEventsAsync(input, timeoutToken).ConfigureAwait(false))
                {
                    hasReceivedEvent = true;
                    var mid = ProcessEvent(evt, fullResponse, thinkingContent);
                    if (mid is not null) lastModelId = mid;
                }
            }

            LastResponse = fullResponse.ToString();

            // 等待思考内容持久化完成，避免 fire-and-forget 丢失
            await StoreThinkingIfAnyAsync(thinkingContent, lastModelId, cancellationToken).ConfigureAwait(false);

            return SessionTurnResult.Success(LastResponse, requestTimestamp);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !hasReceivedEvent)
        {
            await StoreThinkingIfAnyAsync(thinkingContent, lastModelId, CancellationToken.None).ConfigureAwait(false);
            return SessionTurnResult.Timeout(apiTimeoutMs);
        }
        catch (OperationCanceledException)
        {
            LastResponse = fullResponse.ToString();
            await StoreThinkingIfAnyAsync(thinkingContent, lastModelId, CancellationToken.None).ConfigureAwait(false);
            return SessionTurnResult.FromCancellation(LastResponse);
        }
        catch (PermissionPendingConfirmationException)
        {
            LastResponse = fullResponse.ToString();
            throw;
        }
        catch (Exception ex)
        {
            LastResponse = fullResponse.ToString();
            await StoreThinkingIfAnyAsync(thinkingContent, lastModelId, CancellationToken.None).ConfigureAwait(false);

            var crashStore = _serviceProvider?.GetService<ICrashSnapshotStore>();
            if (crashStore is not null && ex is not OperationCanceledException)
            {
                try
                {
                    crashStore.Add(new CrashSnapshot(
                        fenceName: "MainAgent",
                        severity: CrashSeverity.Error,
                        exception: ex,
                        executionContext: new CrashExecutionContext { SessionId = _sessionId }));
                }
                catch (Exception snapshotEx)
                {
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
    /// 处理单个 ChatStreamEvent — 分发到 IEventConsumer，返回 Done 事件的 modelId（否则 null）
    /// </summary>
    private string? ProcessEvent(ChatStreamEvent evt, StringBuilder fullResponse, StringBuilder thinkingContent)
    {
        string? modelId = null;
        evt.Switch(
            onText: content =>
            {
                if (content.Length > 0) fullResponse.Append(content);
                _consumer.OnText(content);
            },
            onThinking: thinking =>
            {
                if (thinking.Length > 0) thinkingContent.Append(thinking);
                _consumer.OnThinking(thinking);
            },
            onToolStart: (toolName, callId, arguments) =>
            {
                _consumer.OnToolStart(toolName, callId, arguments);
            },
            onToolEnd: (toolName, resultText, callId, isToolError, structuredPatch) =>
            {
                _consumer.OnToolEnd(toolName, resultText, callId, isToolError, structuredPatch);
                RecordToolCallForTurnDiff(toolName, resultText, structuredPatch);
            },
            onToolProgress: (toolName, progressType, progressMessage) =>
            {
                _consumer.OnToolProgress(toolName, progressType, progressMessage);
            },
            onLoopDetected: (triggerCount, loopStartIndex, repeatedPattern) =>
            {
                _consumer.OnLoopDetected(triggerCount, loopStartIndex, repeatedPattern);
            },
            onTimingSummary: summary =>
            {
                _consumer.OnTimingSummary(summary);
            },
            onDone: (usage, mid) =>
            {
                modelId = mid;
                _consumer.OnDone(usage, mid);
            });
        return modelId;
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
    private async Task<MainAgentPreprocess> PreProcessMainAgentAsync(string input, CancellationToken ct)
    {
        if (_serviceProvider is null) return new MainAgentPreprocess(null, null, null);

        var fileContextService = _serviceProvider.GetService<IChatFileContextService>();
        fileContextService?.UpdateFileContext(input);

        var preprocessor = _serviceProvider.GetService<IChatPreprocessor>();
        PreprocessResult? preprocessResult = null;
        if (preprocessor is not null)
        {
            preprocessResult = await preprocessor.AnalyzeAndInjectAsync(input, ct).ConfigureAwait(false);
            await preprocessor.PrepareContextAsync(input, false, ct).ConfigureAwait(false);
        }

        var contextManager = _serviceProvider.GetService<IChatContextManager>();
        if (contextManager is not null)
        {
            await contextManager.RecordPromptStateAsync(ct).ConfigureAwait(false);
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
    private string? DetectModalityMismatch(string input)
    {
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

        var missingDesc = FormatMissingModalities(missing);
        var keywordsDesc = string.Join(", ", detection.MatchedKeywords);
        return $"[模态不匹配提示] 当前模型 {modelId} 不支持 {missingDesc}（检测到用户意图: {keywordsDesc}）。";
    }

    private static string FormatMissingModalities(ModelModalityKind missing)
    {
        var parts = new List<string>();
        if (missing.HasFlag(ModelModalityKind.ReadImage)) parts.Add("图片识别");
        if (missing.HasFlag(ModelModalityKind.ReadGif)) parts.Add("动图识别");
        if (missing.HasFlag(ModelModalityKind.ReadVideo)) parts.Add("视频识别");
        if (missing.HasFlag(ModelModalityKind.ReadAudio)) parts.Add("音频识别");
        if (missing.HasFlag(ModelModalityKind.ReadPdf)) parts.Add("PDF识别");
        if (missing.HasFlag(ModelModalityKind.GenerateImage)) parts.Add("图片生成");
        if (missing.HasFlag(ModelModalityKind.GenerateVideo)) parts.Add("视频生成");
        if (missing.HasFlag(ModelModalityKind.GenerateAudio)) parts.Add("音频生成");
        return string.Join("、", parts);
    }

    /// <summary>
    /// 主代理路径后处理 — 对齐 SaveContextMiddleware + CleanupInjectionsMiddleware：持久化上下文 + 清理注入
    /// </summary>
    private async Task PostProcessMainAgentAsync(PreprocessResult? preprocessResult, CancellationToken ct)
    {
        if (_serviceProvider is null) return;

        var contextManager = _serviceProvider.GetService<IChatContextManager>();
        if (contextManager is not null)
        {
            try
            {
                await contextManager.SaveContextAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _serviceProvider.GetService<ILogger<SessionController>>()?.LogError(ex, "[SessionController] 上下文保存失败");
            }
        }

        if (preprocessResult is not null)
        {
            var preprocessor = _serviceProvider.GetService<IChatPreprocessor>();
            if (preprocessor is not null)
            {
                try
                {
                    await preprocessor.CleanupInjectionsAsync(
                        preprocessResult.KeywordResult,
                        preprocessResult.SynonymInjectionIds, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _serviceProvider.GetService<ILogger<SessionController>>()?.LogError(ex, "[SessionController] 清理注入失败");
                }
            }
        }
    }

    /// <summary>
    /// 持久化思考内容 — 等待完成并记录失败，避免 fire-and-forget 静默丢失
    /// </summary>
    private async Task StoreThinkingIfAnyAsync(StringBuilder thinkingContent, string? modelId, CancellationToken ct)
    {
        if (thinkingContent.Length == 0) return;
        var thinkingStore = _serviceProvider?.GetService<IThinkingStore>();
        if (thinkingStore is null) return;
        try
        {
            await thinkingStore.StoreAsync(_sessionId, thinkingContent.ToString(), modelId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var logger = _serviceProvider?.GetService<ILogger<SessionController>>();
            logger?.LogError(ex, "[SessionController] 思考内容持久化失败");
        }
    }

    private static int ParseApiTimeoutMs()
    {
        var env = Environment.GetEnvironmentVariable("JCC_API_TIMEOUT_MS");
        if (int.TryParse(env, out var ms) && ms > 0)
            return ms;
        return 10_000;
    }

    private static int _apiTimeoutLogged;
    private void LogApiTimeoutOnce(int apiTimeoutMs)
    {
        if (Interlocked.Exchange(ref _apiTimeoutLogged, 1) != 0) return;
        var logger = _serviceProvider?.GetService<ILogger<SessionController>>();
        logger?.LogDebug("[SessionController] API timeout: {Ms}ms (JCC_API_TIMEOUT_MS={Env})", apiTimeoutMs, Environment.GetEnvironmentVariable("JCC_API_TIMEOUT_MS") ?? "(未设置)");
    }

    private void RecordToolCallForTurnDiff(string toolName, string? resultText, StructuredPatchHunk[]? structuredPatch)
    {
        var isFileEdit = toolName is FileToolNameConstants.FileWrite or FileToolNameConstants.FileEdit
            or FileToolNameConstants.FileEditRegex or FileToolNameConstants.FileBatchEdit
            or FileToolNameConstants.FileInsertLines or FileToolNameConstants.FileDeleteLines;
        if (!isFileEdit) return;

        var filePath = ExtractFilePathFromResult(resultText);
        if (filePath is null) return;
        var isNewFile = toolName == FileToolNameConstants.FileWrite;

        if (structuredPatch is not null && structuredPatch.Length > 0)
            _turnDiffService.RecordFileEditWithPatch(filePath, structuredPatch, isNewFile);
        else
            _turnDiffService.RecordFileEdit(filePath, resultText, isNewFile);
    }

    private static string? ExtractFilePathFromResult(string? resultText)
    {
        if (string.IsNullOrWhiteSpace(resultText)) return null;
        foreach (var line in resultText.AsSpan().EnumerateLines())
        {
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
public sealed class SessionTurnResult
{
    public bool Succeeded { get; init; }
    public bool TimedOut { get; init; }
    public bool WasCancelled { get; init; }
    public int TimeoutMs { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public bool IsRetryable { get; init; }
    public string Response { get; init; } = string.Empty;
    public DateTime RequestTimestamp { get; init; }

    public static SessionTurnResult Success(string response, DateTime requestTimestamp) => new()
    {
        Succeeded = true,
        Response = response,
        RequestTimestamp = requestTimestamp
    };

    public static SessionTurnResult Timeout(int timeoutMs) => new()
    {
        TimedOut = true,
        TimeoutMs = timeoutMs
    };

    public static SessionTurnResult FromCancellation(string partialResponse) => new()
    {
        WasCancelled = true,
        Response = partialResponse
    };

    public static SessionTurnResult Error(string errorMessage, string partialResponse, string? errorCode = null, bool isRetryable = false) => new()
    {
        ErrorMessage = errorMessage,
        ErrorCode = errorCode,
        IsRetryable = isRetryable,
        Response = partialResponse
    };
}
