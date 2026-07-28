using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 循环干预中间件 — 拦截 LoopDetected 事件，按漏斗级别执行干预策略
/// Level 1(第1~HardTruncateThreshold-1次): 软干预，注入提示词，流继续
/// Level 2(第HardTruncateThreshold~CompactThreshold-1次): 硬截断，撤回+降温度+重连
/// Level 3(第CompactThreshold次+/重连失败): 上下文压缩，无人值守
/// </summary>
[Register]
public sealed partial class LoopInterventionMiddleware : IChatMiddleware
{
    private readonly IChatClient _kernel;
    private readonly IChatContextManager _contextManager;
    private readonly IChatStreamChunkProcessor _chunkProcessor;
    private readonly ITaskProgressTracker? _progressTracker;
    private readonly LoopInterventionOptions _options;
    [Inject] private readonly ILogger<LoopInterventionMiddleware>? _logger;


    public LoopInterventionMiddleware(
        IChatClient kernel,
        IChatContextManager contextManager,
        IChatStreamChunkProcessor chunkProcessor,
        ITaskProgressTracker? progressTracker = null,
        IOptions<LoopInterventionOptions>? options = null,
        ILogger<LoopInterventionMiddleware>? logger = null)
    {
        _kernel = kernel;
        _contextManager = contextManager;
        _chunkProcessor = chunkProcessor;
        _progressTracker = progressTracker;
        _options = options?.Value ?? new LoopInterventionOptions();
        _logger = logger;
    }

    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var hasLoopDetected = false;
        var loopTriggerCount = 0;
        var hasProgressed = false;
        var effectiveTriggerCount = 0;

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            if (evt.Type == ChatStreamEventType.LoopDetected)
            {
                hasLoopDetected = true;
                loopTriggerCount = evt.LoopTriggerCount;
                context.LoopTriggerCount = loopTriggerCount;

                hasProgressed = await CheckTaskProgressAsync(context, ct).ConfigureAwait(false);
                effectiveTriggerCount = hasProgressed
                    ? AdjustTriggerCountForProgress(loopTriggerCount)
                    : loopTriggerCount;

                if (effectiveTriggerCount >= _options.CompactThreshold)
                {
                    _logger?.LogWarning("[LoopInterventionMiddleware] Level 3 上下文压缩，第{N}次循环触发(有效{E})，任务推进={P}",
                        loopTriggerCount, effectiveTriggerCount, hasProgressed);
                    yield return ChatStreamEvent.Text(_options.HardTruncatePrompt);
                    break;
                }

                if (effectiveTriggerCount >= _options.HardTruncateThreshold)
                {
                    _logger?.LogWarning("[LoopInterventionMiddleware] Level 2 硬截断，第{N}次循环触发(有效{E})，任务推进={P}",
                        loopTriggerCount, effectiveTriggerCount, hasProgressed);
                    yield return ChatStreamEvent.Text(_options.HardTruncatePrompt);
                    break;
                }

                _logger?.LogWarning("[LoopInterventionMiddleware] Level 1 软干预，第{N}次循环触发，任务推进={P}，注入提示词后流继续",
                    loopTriggerCount, hasProgressed);
                yield return ChatStreamEvent.Text(_options.SoftIntervenePrompt);
                continue;
            }

            yield return evt;
        }

        if (!hasLoopDetected || effectiveTriggerCount < _options.HardTruncateThreshold)
            yield break;

        if (effectiveTriggerCount >= _options.CompactThreshold)
        {
            await foreach (var evt in CompactAsync(ct).ConfigureAwait(false))
                yield return evt;
            yield break;
        }

        _logger?.LogWarning("[LoopInterventionMiddleware] 开始重连：撤回循环轮次 + 降温度 + 重新发起LLM调用");

        var retrySucceeded = false;

        for (var attempt = 0; attempt < _options.MaxRetryAttempts; attempt++)
        {
            var isLastAttempt = attempt == _options.MaxRetryAttempts - 1;
            var temperature = isLastAttempt ? _options.SecondChanceTemperature : _options.RetryTemperature;

            var rewindResult = await _contextManager.RewindLastTurnAsync(ct).ConfigureAwait(false);
            _logger?.LogInformation("[LoopInterventionMiddleware] 撤回完成：移除{Count}条消息", rewindResult.RemovedCount);

            if (_options.InsertRewindAuditMark && rewindResult.Success)
            {
                var auditMark = $"[系统撤回: 原因=循环检测, 移除消息数={rewindResult.RemovedCount}]";
                await _contextManager.AddSystemMessageAsync(auditMark, ct).ConfigureAwait(false);
                _logger?.LogInformation("[LoopInterventionMiddleware] 已插入撤回审计标记");
            }

            var retrySettings = CreateRetrySettings(context.ExecutionSettings, temperature);
            var retryHasLoop = false;

            var historySnapshot = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
            var chatCompletionService = _kernel.GetChatCompletionService();
            var iterState = _chunkProcessor.CreateIterationState();

            await foreach (var chunk in chatCompletionService.GetStreamEventContentsAsync(
                historySnapshot, retrySettings, _kernel, ct).ConfigureAwait(false))
            {
                var result = _chunkProcessor.ProcessChunk(chunk, iterState);

                foreach (var evt in result.Events)
                {
                    if (evt.Type == ChatStreamEventType.LoopDetected)
                    {
                        retryHasLoop = true;
                        _logger?.LogWarning("[LoopInterventionMiddleware] 重连后仍然检测到循环，第{N}次重试失败(温度={T})", attempt + 1, temperature);
                        yield return ChatStreamEvent.Text("\n\n⚠️ 重连后仍检测到循环输出。");
                        break;
                    }

                    yield return evt;
                }

                if (retryHasLoop) break;
                if (result.Action == ChunkAction.Break) break;
                if (result.Action == ChunkAction.Continue) continue;
            }

            if (iterState.StreamUsage is not null)
                context.FinalUsage = iterState.StreamUsage;
            if (iterState.StreamModelId is not null)
                context.FinalModelId = iterState.StreamModelId;

            if (!retryHasLoop)
            {
                _logger?.LogInformation("[LoopInterventionMiddleware] 重连成功，循环已打破(温度={T})", temperature);
                retrySucceeded = true;

                if (iterState.ToolCallName is null)
                {
                    var aiResponse = iterState.FullResponse.ToString();
                    if (!string.IsNullOrEmpty(aiResponse))
                    {
                        await _contextManager.AddAssistantMessageAsync(aiResponse, ct).ConfigureAwait(false);
                    }
                }

                yield break;
            }
        }

        if (!retrySucceeded)
        {
            _logger?.LogWarning("[LoopInterventionMiddleware] 重连{Max}次后仍然循环，进入Level 3上下文压缩", _options.MaxRetryAttempts);
            yield return ChatStreamEvent.Text(_options.CompactPrompt);

            await foreach (var evt in CompactAsync(ct).ConfigureAwait(false))
                yield return evt;
        }
    }

    private async IAsyncEnumerable<ChatStreamEvent> CompactAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        string? lastUserMessage = null;

        if (_options.PreserveLastUserMessageOnReset)
        {
            lastUserMessage = await ExtractLastUserMessageAsync(ct).ConfigureAwait(false);
        }

        var compactResult = await _contextManager.FoldIfNeededAsync(
            _options.CompactFoldDecision, ct).ConfigureAwait(false);

        if (compactResult.Folded)
        {
            _logger?.LogInformation("[LoopInterventionMiddleware] 上下文压缩完成，原始{Orig}条，保留头{Head}+尾{Tail}条",
                compactResult.OriginalMessageCount, compactResult.HeadMessageCount, compactResult.TailMessageCount);
            yield return ChatStreamEvent.Text(_options.CompactSuccessPrompt);
        }
        else
        {
            _logger?.LogWarning("[LoopInterventionMiddleware] 上下文压缩失败，强制撤回到起点");

            await _contextManager.RewindToStartAsync(ct).ConfigureAwait(false);

            if (lastUserMessage is not null)
            {
                await _contextManager.AddSystemMessageAsync(
                    "对话因循环检测已重置，以下是用户最近的需求描述：", ct).ConfigureAwait(false);
                await _contextManager.AddUserMessageAsync(lastUserMessage, ct).ConfigureAwait(false);
                await _contextManager.AddSystemMessageAsync("请继续。", ct).ConfigureAwait(false);
                _logger?.LogInformation("[LoopInterventionMiddleware] 已保留最近1轮用户消息作为种子");
            }

            yield return ChatStreamEvent.Text(_options.CompactFallbackPrompt);
        }
    }

    private async Task<string?> ExtractLastUserMessageAsync(CancellationToken ct)
    {
        try
        {
            var messages = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
            for (var i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == MessageRole.User && !string.IsNullOrEmpty(messages[i].Content))
                {
                    return messages[i].Content;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[LoopInterventionMiddleware] 提取最近用户消息失败");
        }

        return null;
    }

    private ChatOptions CreateRetrySettings(ChatOptions? original, float temperature)
    {
        return new ChatOptions
        {
            Temperature = temperature,
            MaxTokens = original?.MaxTokens,
            TopP = original?.TopP,
            FrequencyPenalty = original?.FrequencyPenalty,
            PresencePenalty = original?.PresencePenalty,
            ToolChoice = original?.ToolChoice ?? ToolChoice.AutoInvoke,
            DiscoveredTools = original?.DiscoveredTools,
            DeferredTools = original?.DeferredTools,
            ExtensionData = original?.ExtensionData,
            EffortLevel = original?.EffortLevel,
            FastMode = original?.FastMode ?? false,
            FastModelId = original?.FastModelId,
            ContextManagement = original?.ContextManagement
        };
    }

    private async Task<bool> CheckTaskProgressAsync(ChatMiddlewareContext context, CancellationToken ct)
    {
        if (_progressTracker is null)
            return false;

        try
        {
            var hasProgressed = await _progressTracker.HasProgressedSinceLastSnapshotAsync(ct).ConfigureAwait(false);
            var currentCount = await _progressTracker.GetCompletedTodoCountAsync(ct).ConfigureAwait(false);

            context.PreviousCompletedTodoCount = context.CurrentCompletedTodoCount;
            context.CurrentCompletedTodoCount = currentCount;
            context.HasTaskProgressed = hasProgressed;

            await _progressTracker.SnapshotCurrentProgressAsync(ct).ConfigureAwait(false);

            return hasProgressed;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[LoopInterventionMiddleware] 检查任务进度失败，假定无推进");
            return false;
        }
    }

    private int AdjustTriggerCountForProgress(int loopTriggerCount)
    {
        return Math.Max(1, loopTriggerCount - _options.ProgressDiscount);
    }
}
