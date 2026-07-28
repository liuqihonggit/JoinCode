namespace Core.Context;

/// <summary>
/// 聊天预处理结果 — 关键词/同义词注入后的上下文信息
/// </summary>
public sealed record PreprocessResult
{
    /// <summary>关键词分析结果</summary>
    public required UserPromptKeywordResult KeywordResult { get; init; }

    /// <summary>同义词注入 ID 列表（用于后续清理）</summary>
    public required List<string> SynonymInjectionIds { get; init; }

    /// <summary>注入提示信息（展示给用户）</summary>
    public string? PromptInjectionInfo { get; init; }
}

/// <summary>
/// ChatPreprocessor 可选依赖聚合
/// </summary>
public sealed record ChatPreprocessorDependencies
{
    public Prompts.Services.ToolListingService? ToolListingService { get; init; }
    public JoinCode.Abstractions.Interfaces.Lsp.ILspDiagnosticProvider? LspDiagnosticProvider { get; init; }
}

/// <summary>
/// 聊天预处理器 — 薄协调层，通过中间件管道执行预处理步骤
/// </summary>
public sealed partial class ChatPreprocessor : IChatPreprocessor
{
    private readonly MiddlewarePipeline<PreprocessContext> _analyzePipeline;
    private readonly MiddlewarePipeline<PreprocessContext> _preparePipeline;
    private readonly ISystemReminderManager _reminderManager;
    private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<ChatPreprocessor>? _logger;

    public ChatPreprocessor(
        MiddlewarePipeline<PreprocessContext> analyzePipeline,
        MiddlewarePipeline<PreprocessContext> preparePipeline,
        ISystemReminderManager reminderManager,
        IChatContextManager contextManager,
        ILogger<ChatPreprocessor>? logger = null)
    {
        _analyzePipeline = analyzePipeline ?? throw new ArgumentNullException(nameof(analyzePipeline));
        _preparePipeline = preparePipeline ?? throw new ArgumentNullException(nameof(preparePipeline));
        _reminderManager = reminderManager;
        _contextManager = contextManager;
        _logger = logger;
    }

    /// <summary>
    /// 分析并注入关键词和同义词，返回预处理结果
    /// </summary>
    public async Task<PreprocessResult> AnalyzeAndInjectAsync(string message, CancellationToken ct)
    {
        var context = new PreprocessContext { Message = message };
        await _analyzePipeline.ExecuteAsync(context, ct).ConfigureAwait(false);

        return new PreprocessResult
        {
            KeywordResult = context.KeywordResult,
            SynonymInjectionIds = context.SynonymInjectionIds,
            PromptInjectionInfo = context.PromptInjectionInfo
        };
    }

    /// <summary>
    /// 准备上下文：构建系统提示、注入提醒、添加用户消息
    /// </summary>
    public async Task PrepareContextAsync(string message, bool isDryRun = false, CancellationToken ct = default)
    {
        var context = new PreprocessContext { Message = message };
        await _preparePipeline.ExecuteAsync(context, ct).ConfigureAwait(false);

        if (!isDryRun)
            await _contextManager.AddUserMessageAsync(message, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 清理注入的关键词和同义词提醒
    /// </summary>
    public async Task CleanupInjectionsAsync(UserPromptKeywordResult keywordResult, List<string> synonymInjectionIds, CancellationToken ct)
    {
        if (keywordResult.HasPromptInjection)
        {
            try
            {
                var injectionIds = (await _reminderManager.GetRemindersAsync(ct)
                    .ConfigureAwait(false))
                    .Where(r => r.Id.StartsWith("user-prompt-injection-", StringComparison.Ordinal)
                             || r.Id.StartsWith("section-injection-", StringComparison.Ordinal))
                    .Select(r => r.Id)
                    .ToList();

                if (injectionIds.Count > 0)
                {
                    await Task.WhenAll(injectionIds.Select(id =>
                        _reminderManager.RemoveReminderAsync(id, ct))).ConfigureAwait(false);
                    foreach (var id in injectionIds)
                    {
                        _logger?.LogDebug("[UserPromptInjection] 已清理临时提示词: {Id}", id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ChatPreprocessor] 清理注入提醒失败");
            }
        }

        if (synonymInjectionIds.Count > 0)
        {
            try
            {
                await Task.WhenAll(synonymInjectionIds.Select(id =>
                    _reminderManager.RemoveReminderAsync(id, ct))).ConfigureAwait(false);
                foreach (var id in synonymInjectionIds)
                {
                    _logger?.LogDebug("[SynonymInjection] 已清理同义词补充: {Id}", id);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ChatPreprocessor] 清理同义词补充失败");
            }
        }
    }
}
