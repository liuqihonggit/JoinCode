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
        IClockService? clock = null)
    {
        _chatService = chatService;
        _consumer = consumer;
        _turnDiffService = turnDiffService;
        _sessionId = sessionId;
        _serviceProvider = serviceProvider;
        _clock = clock ?? SystemClockService.Instance;
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

        const int ApiTimeoutMs = 10_000;
        using var timeoutCts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromMilliseconds(ApiTimeoutMs));
        var timeoutToken = timeoutCts.Token;
        var hasReceivedEvent = false;

        try
        {
            await foreach (var evt in _chatService.StreamWithEventsAsync(input, timeoutToken).ConfigureAwait(false))
            {
                hasReceivedEvent = true;
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
                    onToolStart: (toolName, _, arguments) =>
                    {
                        _consumer.OnToolStart(toolName, _, arguments);
                    },
                    onToolEnd: (toolName, resultText, _, isToolError, structuredPatch) =>
                    {
                        _consumer.OnToolEnd(toolName, resultText, _, isToolError, structuredPatch);
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
                    onDone: (usage, modelId) =>
                    {
                        lastModelId = modelId;
                        _consumer.OnDone(usage, modelId);
                        if (thinkingContent.Length > 0)
                        {
                            var thinkingStore = _serviceProvider?.GetService<IThinkingStore>();
                            if (thinkingStore != null)
                            {
                                _ = thinkingStore.StoreAsync(_sessionId, thinkingContent.ToString(), lastModelId, cancellationToken);
                            }
                        }
                    });
            }

            LastResponse = fullResponse.ToString();
            return SessionTurnResult.Success(LastResponse, requestTimestamp);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !hasReceivedEvent)
        {
            return SessionTurnResult.Timeout();
        }
        catch (OperationCanceledException)
        {
            LastResponse = fullResponse.ToString();
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
            if (ex is JoinCode.Abstractions.Exceptions.ApiException apiEx)
                return SessionTurnResult.Error(apiEx.Message, LastResponse, apiEx.ErrorCode, apiEx.IsRetryable);
            if (ex is JoinCode.Abstractions.Exceptions.WorkflowException wfEx)
                return SessionTurnResult.Error(wfEx.Message, LastResponse, wfEx.ErrorCode);
            return SessionTurnResult.Error(ex.Message, LastResponse);
        }
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

    public static SessionTurnResult Timeout() => new()
    {
        TimedOut = true
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
