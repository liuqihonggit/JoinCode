namespace Core.Context;

/// <summary>
/// 内容替换结果 — 工具调用后的内容替换和预算应用结果
/// </summary>
public sealed record ContentReplacementResult
{
    /// <summary>
    /// 替换后的工具结果文本（null 表示无需替换）
    /// </summary>
    public string? EffectiveToolResult { get; init; }

    /// <summary>
    /// 预算处理后的消息列表（null 表示无需替换消息）
    /// </summary>
    public IReadOnlyList<ApiMessage>? BudgetedMessages { get; init; }

    /// <summary>
    /// 本次新产生的替换记录
    /// </summary>
    public required IReadOnlyList<ContentReplacementRecord> NewlyReplaced { get; init; }
}

/// <summary>
/// 聊天内容替换处理器 — 对齐 TS maybePersistLargeToolResult + applyToolResultBudget
/// 负责超大工具结果持久化、per-message 预算检查、transcript 记录
/// </summary>
[Register]
public sealed partial class ChatContentReplacer : IChatContentReplacer
{
    [Inject] private readonly IContentReplacementService? _contentReplacementService;
    [Inject] private readonly ITranscriptService? _transcriptService;
    [Inject] private readonly ILogger<ChatContentReplacer>? _logger;

    /// <summary>
    /// 对齐 TS provisionContentReplacementState — 初始化内容替换状态
    /// 功能开关关闭时返回 null，query 会跳过整个预算执行
    /// </summary>
    public ContentReplacementState? ProvisionState(IReadOnlyList<ApiMessage>? initialMessages = null)
    {
        if (_contentReplacementService is null) return null;
        return _contentReplacementService.ProvisionContentReplacementState(initialMessages);
    }

    /// <summary>
    /// 对齐 TS maybePersistLargeToolResult — 即时持久化超大工具结果
    /// 纯函数：不修改 state，仅返回替换字符串
    /// </summary>
    public string? MaybePersistLargeToolResult(string toolName, string toolUseId, string content, string sessionId)
    {
        if (_contentReplacementService is null) return null;
        return _contentReplacementService.MaybePersistLargeToolResult(toolName, toolUseId, content, sessionId);
    }

    /// <summary>
    /// 对齐 TS applyToolResultBudget — 每轮工具调用后检查 per-message 预算
    /// 包含 transcript 持久化逻辑（对齐 TS writeToTranscript / recordContentReplacement）
    /// </summary>
    public async Task<ContentReplacementResult> ApplyBudgetAsync(
        IReadOnlyList<ApiMessage> messages,
        ContentReplacementState state,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_contentReplacementService is null || state is null)
        {
            return new ContentReplacementResult { NewlyReplaced = [] };
        }

        var (budgeted, newlyReplaced) = await _contentReplacementService.ApplyToolResultBudgetAsync(
            messages, state, sessionId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // 对齐 TS writeToTranscript — 持久化 newlyReplaced 记录到 transcript
        if (newlyReplaced.Count > 0)
        {
            _logger?.LogDebug("Budget replaced {Count} tool results", newlyReplaced.Count);

            // 对齐 TS recordContentReplacement — 持久化到 transcript JSONL
            if (_transcriptService is not null)
            {
                try
                {
                    await _transcriptService.InsertContentReplacementAsync(sessionId, newlyReplaced, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to persist content replacement records to transcript");
                }
            }
        }

        // 仅当消息有变化时返回预算消息列表
        IReadOnlyList<ApiMessage>? budgetedMessages = null;
        if (newlyReplaced.Count > 0 || budgeted.Count != messages.Count)
        {
            budgetedMessages = budgeted;
        }

        return new ContentReplacementResult
        {
            BudgetedMessages = budgetedMessages,
            NewlyReplaced = newlyReplaced
        };
    }
}
