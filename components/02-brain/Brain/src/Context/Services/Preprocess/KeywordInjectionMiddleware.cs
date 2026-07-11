using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 关键词注入中间件 — 检测用户输入关键词并注入对应提示词
/// </summary>
[Register(typeof(IAnalyzePreprocessMiddleware))]
public sealed partial class KeywordInjectionMiddleware : IAnalyzePreprocessMiddleware
{
    [Inject] private readonly ISystemReminderManager _reminderManager;
    [Inject] private readonly ILogger<KeywordInjectionMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct)
    {
        var keywordResult = UserPromptKeywordAnalyzer.AnalyzeInput(context.Message);
        context.KeywordResult = keywordResult;

        if (keywordResult.HasPromptInjection)
        {
            _logger?.LogDebug("[UserPromptInjection] 检测到关键词 '{Keyword}'，类型: {Type}",
                keywordResult.MatchedKeyword, keywordResult.Type);

            var injectionId = $"user-prompt-injection-{keywordResult.Type}";
            await _reminderManager.AddReminderAsync(
                injectionId,
                keywordResult.SuggestedPrompt,
                priority: 100,
                ct: ct).ConfigureAwait(false);

            var sectionContent = KeywordSectionMapper.GetSectionContentForKeywordType(keywordResult.Type);
            if (sectionContent != null)
            {
                var sectionId = $"section-injection-{keywordResult.Type}";
                await _reminderManager.AddReminderAsync(
                    sectionId,
                    sectionContent,
                    priority: 90,
                    ct: ct).ConfigureAwait(false);
            }

            _logger?.LogInformation("[UserPromptInjection] 已注入 {Type} 提示词", keywordResult.Type);

            context.KeywordPromptInjectionInfo = $"[系统提示: 检测到 '{keywordResult.MatchedKeyword}' 关键词，已自动注入 {keywordResult.Type} 提示词]";
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
